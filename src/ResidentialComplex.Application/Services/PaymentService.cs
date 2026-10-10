using ResidentialComplex.Application.DTOs;
using ResidentialComplex.Application.Helpers;
using ResidentialComplex.Application.Interfaces;
using ResidentialComplex.Domain.Entities;
using ResidentialComplex.Domain.Enums;

namespace ResidentialComplex.Application.Services;

/// <summary>
/// Online bill payment orchestration (start, gateway callback, reconcile).
/// See Docs/README.md §3 for the flow and the security rules implemented here:
/// callback data is never trusted, the gateway answer decides; amount/order are bound to
/// what we stored; settlement is exactly-once (atomic claim + conditional bill update + one transaction).
/// </summary>
public sealed class PaymentService
{
    /// <summary>Max attempts per bill inside <see cref="AttemptWindow"/>.</summary>
    public const int MaxAttemptsPerWindow = 5;
    public static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(10);

    /// <summary>A Verifying claim older than this is considered abandoned (crashed worker).</summary>
    public static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(2);

    /// <summary>An attempt still unpaid at the gateway after this long is marked Expired.</summary>
    public static readonly TimeSpan ExpireAfter = TimeSpan.FromMinutes(60);

    /// <summary>An attempt that never got a trackId after this long is marked RequestFailed.</summary>
    public static readonly TimeSpan InitiatedTimeout = TimeSpan.FromMinutes(10);

    /// <summary>Zibal requires amount greater than 1,000 Rial.</summary>
    public const long MinAmountRial = 1000;

    private const int MaxStoredDataLength = 8000;
    private const string SystemUserId = "system";
    private const string SystemUserName = "درگاه پرداخت";

    private readonly IPaymentAttemptRepository _attempts;
    private readonly IBillRepository _bills;
    private readonly IHouseRepository _houses;
    private readonly IPaymentRepository _payments;
    private readonly IPaymentGateway _gateway;
    private readonly ITransactionRunner _tx;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;

    public PaymentService(
        IPaymentAttemptRepository attempts,
        IBillRepository bills,
        IHouseRepository houses,
        IPaymentRepository payments,
        IPaymentGateway gateway,
        ITransactionRunner tx,
        IAuditService audit,
        TimeProvider? timeProvider = null)
    {
        _attempts = attempts;
        _bills = bills;
        _houses = houses;
        _payments = payments;
        _gateway = gateway;
        _tx = tx;
        _audit = audit;
        _clock = timeProvider ?? TimeProvider.System;
    }

    private DateTime Now => _clock.GetLocalNow().DateTime;

    public bool IsGatewayConfigured => _gateway.IsConfigured;

    // =====================================================================
    // 1. Start
    // =====================================================================

    /// <summary>
    /// Creates a payment attempt for an approved bill of the given user and opens a gateway session.
    /// </summary>
    public async Task<StartPaymentResult> StartPaymentAsync(
        int billId, string userId, string userName, string callbackUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return StartPaymentResult.Fail("کاربر شناسایی نشد.");
        if (!_gateway.IsConfigured)
            return StartPaymentResult.Fail("درگاه پرداخت آنلاین پیکربندی نشده است. لطفاً با مدیر سیستم تماس بگیرید.");
        if (!IsValidCallbackUrl(callbackUrl))
            return StartPaymentResult.Fail("آدرس بازگشت از درگاه نامعتبر است.");

        var bill = await _bills.GetByIdAsync(billId);
        if (bill is null)
            return StartPaymentResult.Fail("قبض یافت نشد.");

        // Ownership: only the resident linked to the house may pay its bills.
        if (bill.House is null || string.IsNullOrEmpty(bill.House.ApplicationUserId) || bill.House.ApplicationUserId != userId)
        {
            await SafeAuditAsync(userId, userName, nameof(PaymentAttempt), "-", "PaymentStartDenied", null, $"BillId={billId}");
            return StartPaymentResult.Fail("شما مجاز به پرداخت این قبض نیستید.");
        }

        if (bill.Status == BillStatus.Paid)
            return StartPaymentResult.Fail("این قبض قبلاً پرداخت شده است.", alreadyPaid: true);
        if (bill.Status != BillStatus.Approved)
            return StartPaymentResult.Fail("این قبض هنوز تایید نشده و قابل پرداخت نیست.");

        if (bill.TotalAmount != decimal.Truncate(bill.TotalAmount) || bill.TotalAmount <= MinAmountRial)
            return StartPaymentResult.Fail("مبلغ قبض برای پرداخت آنلاین معتبر نیست.");
        var amountRial = (long)bill.TotalAmount;

        // Abuse guard.
        var now = Now;
        if (await _attempts.CountCreatedSinceAsync(billId, now - AttemptWindow) >= MaxAttemptsPerWindow)
            return StartPaymentResult.Fail("تعداد تلاش‌های پرداخت بیش از حد مجاز است. لطفاً چند دقیقه بعد دوباره تلاش کنید.");

        // Earlier open attempts may actually have been paid (callback lost, tab closed at the bank).
        foreach (var open in (await _attempts.GetByBillIdAsync(billId)).Where(a => IsOpen(a.Status) && a.TrackId.HasValue))
        {
            var r = await ProcessAsync(open, userId, userName, ct);
            if (r.Outcome == PaymentProcessingOutcome.Succeeded)
                return StartPaymentResult.Fail("پرداخت قبلی شما تایید شد و قبض پرداخت شده است.", alreadyPaid: true, attemptPublicId: open.PublicId);
        }

        var attempt = await _attempts.AddAsync(new PaymentAttempt
        {
            PublicId = Guid.NewGuid(),
            BillId = billId,
            Amount = amountRial,
            Gateway = _gateway.Name,
            OrderId = Guid.NewGuid().ToString("N"),
            Status = PaymentAttemptStatus.Initiated,
            CreatedAt = now,
            UpdatedAt = now,
            InitiatedByUserId = userId
        });
        await AddEventAsync(attempt.Id, PaymentEventType.Created, null, null, $"شروع پرداخت مبلغ {amountRial:N0} ریال", null);

        var description = $"پرداخت قبض {PersianCalendarHelper.FormatYearMonth(bill.Year, bill.Month)} - {bill.House.Title}";
        GatewayRequestResult request;
        try
        {
            request = await _gateway.RequestPaymentAsync(
                new GatewayRequest(amountRial, callbackUrl, attempt.OrderId, description, NormalizeMobile(bill.House.ResidentPhoneNumber)), ct);
        }
        catch (Exception ex)
        {
            request = new GatewayRequestResult { TransportError = true, Message = ex.GetType().Name };
        }

        await AddEventAsync(attempt.Id, PaymentEventType.GatewayRequest, request.ResultCode, null,
            request.TransportError ? "خطا در برقراری ارتباط با درگاه" : ZibalStatusCodes.DescribeRequestResult(request.ResultCode), request.RawData);

        attempt.RequestResultCode = request.TransportError ? null : request.ResultCode;
        attempt.UpdatedAt = Now;

        if (!request.Success)
        {
            attempt.Status = PaymentAttemptStatus.RequestFailed;
            attempt.Note = request.TransportError
                ? "ارتباط با درگاه برقرار نشد."
                : $"درگاه درخواست را نپذیرفت: {ZibalStatusCodes.DescribeRequestResult(request.ResultCode)}";
            await _attempts.UpdateAsync(attempt);
            await SafeAuditAsync(userId, userName, nameof(PaymentAttempt), attempt.Id.ToString(), "PaymentRequestFailed", null, attempt.Note);
            return StartPaymentResult.Fail("امکان اتصال به درگاه پرداخت وجود ندارد. لطفاً دقایقی بعد دوباره تلاش کنید.", attemptPublicId: attempt.PublicId);
        }

        attempt.TrackId = request.TrackId;
        attempt.Status = PaymentAttemptStatus.Requested;
        attempt.RequestedAt = Now;
        await _attempts.UpdateAsync(attempt);
        await SafeAuditAsync(userId, userName, nameof(PaymentAttempt), attempt.Id.ToString(), "PaymentStarted", null,
            $"BillId={billId}, Amount={amountRial}, TrackId={request.TrackId}");

        return new StartPaymentResult
        {
            Success = true,
            AttemptPublicId = attempt.PublicId,
            RedirectUrl = _gateway.GetStartUrl(request.TrackId!.Value)
        };
    }

    // =====================================================================
    // 2. Callback (user returned from the bank page)
    // =====================================================================

    /// <summary>
    /// Handles the gateway redirect. The callback values are only recorded; the payment is
    /// confirmed (or rejected) exclusively from the gateway's inquiry/verify answer.
    /// </summary>
    public async Task<PaymentProcessingResult> HandleCallbackAsync(PaymentCallback callback, CancellationToken ct = default)
    {
        var attempt = await _attempts.GetByTrackIdAsync(callback.TrackId);
        if (attempt is null)
            return new PaymentProcessingResult(PaymentProcessingOutcome.UnknownTrack, null, "پرداختی با این کد پیگیری یافت نشد.");

        bool? success = callback.Success switch { "1" => true, "0" => false, _ => null };
        int? status = int.TryParse(callback.Status, out var s) ? s : null;

        await _attempts.TryMarkCallbackReceivedAsync(attempt.Id, success, status, Now);
        await AddEventAsync(attempt.Id, PaymentEventType.Callback, null, status,
            $"بازگشت از درگاه (success={callback.Success ?? "-"}, status={callback.Status ?? "-"}) — صرفاً ثبت می‌شود و مبنای تصمیم نیست",
            callback.RawQuery);

        if (!string.IsNullOrEmpty(callback.OrderId) && !string.Equals(callback.OrderId, attempt.OrderId, StringComparison.Ordinal))
        {
            await AddEventAsync(attempt.Id, PaymentEventType.Warning, null, null,
                "orderId موجود در callback با سفارش ثبت‌شده مطابقت ندارد (نادیده گرفته شد؛ تایید فقط از پاسخ درگاه انجام می‌شود).", null);
        }

        var fresh = await _attempts.GetByIdAsync(attempt.Id) ?? attempt;
        return await ProcessAsync(fresh, SystemUserId, SystemUserName, ct);
    }

    // =====================================================================
    // 3. Reconcile (manual "استعلام وضعیت" / automatic before a new attempt)
    // =====================================================================

    /// <summary>Re-checks an attempt with the gateway and finishes it if it has been paid.</summary>
    public async Task<PaymentProcessingResult> ReconcileAsync(int attemptId, string userId, string userName, CancellationToken ct = default)
    {
        var attempt = await _attempts.GetByIdAsync(attemptId);
        if (attempt is null)
            return new PaymentProcessingResult(PaymentProcessingOutcome.UnknownTrack, null, "تلاش پرداخت یافت نشد.");
        return await ProcessAsync(attempt, userId, userName, ct);
    }

    /// <summary>
    /// Same as <see cref="ReconcileAsync"/> but for a user-facing request: the attempt is looked up by its
    /// public id and only processed when the user owns it (or is an administrator), so ownership is enforced
    /// here and not only in the UI.
    /// </summary>
    public async Task<PaymentProcessingResult> ReconcileForUserAsync(
        Guid publicId, string userId, string userName, bool isAdministrator, CancellationToken ct = default)
    {
        var visible = await GetForUserAsync(publicId, userId, isAdministrator);
        if (visible is null)
            return new PaymentProcessingResult(PaymentProcessingOutcome.UnknownTrack, null, "پرداخت یافت نشد.");

        // Work on a lightweight copy (no navigation graph) like the other flows.
        var attempt = await _attempts.GetByIdAsync(visible.Id);
        return attempt is null
            ? new PaymentProcessingResult(PaymentProcessingOutcome.UnknownTrack, null, "پرداخت یافت نشد.")
            : await ProcessAsync(attempt, userId, userName, ct);
    }

    /// <summary>
    /// Loads an attempt for display only if the user may see it: administrators see everything,
    /// a resident only attempts for bills of houses linked to his/her account.
    /// </summary>
    public async Task<PaymentAttempt?> GetForUserAsync(Guid publicId, string userId, bool isAdministrator)
    {
        var attempt = await _attempts.GetByPublicIdAsync(publicId);
        if (attempt is null) return null;
        if (isAdministrator) return attempt;
        var ownerId = attempt.Bill?.House?.ApplicationUserId;
        return !string.IsNullOrEmpty(ownerId) && ownerId == userId ? attempt : null;
    }

    // =====================================================================
    // Core processing
    // =====================================================================

    private async Task<PaymentProcessingResult> ProcessAsync(PaymentAttempt attempt, string userId, string userName, CancellationToken ct)
    {
        if (IsFinal(attempt.Status))
            return Final(attempt);

        if (attempt.TrackId is null)
        {
            // Never reached the gateway successfully.
            if (attempt.Status == PaymentAttemptStatus.Initiated && Now - attempt.CreatedAt > InitiatedTimeout)
            {
                attempt.Status = PaymentAttemptStatus.RequestFailed;
                attempt.Note = "درخواست پرداخت به درگاه تکمیل نشد.";
                attempt.UpdatedAt = Now;
                await _attempts.UpdateAsync(attempt);
                return Final(attempt);
            }
            return Result(PaymentProcessingOutcome.Pending, attempt, "درخواست پرداخت هنوز به درگاه ارسال نشده است.");
        }

        var now = Now;
        if (!await _attempts.TryClaimAsync(attempt.Id, now, now - ClaimLease))
        {
            var current = await _attempts.GetByIdAsync(attempt.Id) ?? attempt;
            return IsFinal(current.Status)
                ? Final(current)
                : Result(PaymentProcessingOutcome.InProgress, current, "پرداخت در حال بررسی است. چند لحظه بعد وضعیت را بروزرسانی کنید.");
        }

        var claimed = await _attempts.GetByIdAsync(attempt.Id) ?? attempt;
        // Where to put the attempt back if something transient goes wrong.
        var fallback = attempt.Status is PaymentAttemptStatus.Requested or PaymentAttemptStatus.Verified
            ? attempt.Status
            : PaymentAttemptStatus.CallbackReceived;

        try
        {
            return await ProcessClaimedAsync(claimed, fallback, userId, userName, ct, f => fallback = f);
        }
        catch (Exception ex)
        {
            await _attempts.TryReleaseAsync(claimed.Id, fallback, $"خطای موقت در بررسی پرداخت: {ex.GetType().Name}", Now);
            await AddEventAsync(claimed.Id, PaymentEventType.Warning, null, null, $"خطای موقت: {ex.GetType().Name}: {Truncate(ex.Message, 300)}", null);
            return Result(PaymentProcessingOutcome.TemporaryError, claimed,
                "بررسی پرداخت با خطای موقت مواجه شد. چند لحظه بعد دکمه «استعلام وضعیت» را بزنید.");
        }
    }

    private async Task<PaymentProcessingResult> ProcessClaimedAsync(
        PaymentAttempt attempt, PaymentAttemptStatus fallback, string userId, string userName,
        CancellationToken ct, Action<PaymentAttemptStatus> setFallback)
    {
        var trackId = attempt.TrackId!.Value;

        // 1) Authoritative status from the gateway.
        var inquiry = await _gateway.InquiryAsync(trackId, ct);
        await AddEventAsync(attempt.Id, PaymentEventType.Inquiry, inquiry.ResultCode, inquiry.Status,
            inquiry.TransportError ? "خطا در برقراری ارتباط با درگاه" : ZibalStatusCodes.DescribeStatus(inquiry.Status), inquiry.RawData);

        if (inquiry.TransportError)
            return await ReleaseAsync(attempt, fallback, "ارتباط با درگاه برقرار نشد.", PaymentProcessingOutcome.TemporaryError);

        if (inquiry.ResultCode == ZibalStatusCodes.ResultInvalidTrackId)
            return await FinishAsync(attempt, PaymentAttemptStatus.Failed, "کد پیگیری نزد درگاه نامعتبر است.", PaymentProcessingOutcome.Failed, inquiry);

        if (inquiry.ResultCode != ZibalStatusCodes.ResultOk || inquiry.Status is null)
            return await ReleaseAsync(attempt, fallback, $"پاسخ نامعتبر از درگاه (result={inquiry.ResultCode}).", PaymentProcessingOutcome.TemporaryError);

        var status = inquiry.Status.Value;
        GatewayTransactionInfo paid;

        if (status == ZibalStatusCodes.Waiting)
        {
            if (Now - attempt.CreatedAt > ExpireAfter)
                return await FinishAsync(attempt, PaymentAttemptStatus.Expired, "پرداخت در مهلت مقرر انجام نشد.", PaymentProcessingOutcome.Failed, inquiry);
            return await ReleaseAsync(attempt, fallback, null, PaymentProcessingOutcome.Pending, "پرداخت هنوز تکمیل نشده است.");
        }

        if (ZibalStatusCodes.IsPermanentFailure(status))
            return await FinishAsync(attempt, PaymentAttemptStatus.Failed, ZibalStatusCodes.DescribeStatus(status), PaymentProcessingOutcome.Failed, inquiry);

        if (status == ZibalStatusCodes.PaidNotVerified)
        {
            // 2) Paid but not confirmed yet: we must call verify (only ever on this branch).
            var verify = await _gateway.VerifyAsync(trackId, ct);
            await AddEventAsync(attempt.Id, PaymentEventType.Verify, verify.ResultCode, verify.Status,
                verify.TransportError ? "خطا در برقراری ارتباط با درگاه" : ZibalStatusCodes.DescribeVerifyResult(verify.ResultCode), verify.RawData);

            if (verify.TransportError)
                return await ReleaseAsync(attempt, fallback, "ارتباط با درگاه هنگام تایید برقرار نشد.", PaymentProcessingOutcome.TemporaryError);

            if (verify.ResultCode != ZibalStatusCodes.ResultOk && verify.ResultCode != ZibalStatusCodes.ResultAlreadyVerified)
                return await ReleaseAsync(attempt, fallback,
                    $"تایید پرداخت انجام نشد: {ZibalStatusCodes.DescribeVerifyResult(verify.ResultCode)}", PaymentProcessingOutcome.TemporaryError);

            paid = Merge(verify, inquiry);
        }
        else if (status == ZibalStatusCodes.PaidVerified)
        {
            // Already verified (e.g. we crashed after verify) — continue with settlement only.
            paid = inquiry;
        }
        else
        {
            return await ReleaseAsync(attempt, fallback, $"وضعیت نامشخص از درگاه: {status}", PaymentProcessingOutcome.TemporaryError);
        }

        // 3) Bind the payment to what we stored.
        var now = Now;
        attempt.ZibalStatus = ZibalStatusCodes.PaidVerified;
        attempt.RefNumber = paid.RefNumber ?? attempt.RefNumber;
        attempt.CardNumber = paid.CardNumber ?? attempt.CardNumber;
        attempt.PaidAt = paid.PaidAt ?? attempt.PaidAt;
        attempt.PaidAmount = paid.Amount;
        attempt.VerifiedAt = paid.VerifiedAt ?? now;
        attempt.UpdatedAt = now;

        string? mismatch = null;
        if (paid.Amount is null || paid.Amount.Value != (long)attempt.Amount)
            mismatch = $"مبلغ تاییدشده ({paid.Amount?.ToString("N0") ?? "-"}) با مبلغ ثبت‌شده ({attempt.Amount:N0}) مطابقت ندارد.";
        else if (!string.IsNullOrEmpty(paid.OrderId) && !string.Equals(paid.OrderId, attempt.OrderId, StringComparison.Ordinal))
            mismatch = "شناسه سفارش تاییدشده با سفارش ثبت‌شده مطابقت ندارد.";

        if (mismatch is not null)
        {
            attempt.Status = PaymentAttemptStatus.NeedsReview;
            attempt.Note = mismatch;
            await _attempts.UpdateAsync(attempt);
            await AddEventAsync(attempt.Id, PaymentEventType.Warning, null, paid.Status, mismatch, null);
            await SafeAuditAsync(userId, userName, nameof(PaymentAttempt), attempt.Id.ToString(), "PaymentNeedsReview", null, mismatch);
            return Result(PaymentProcessingOutcome.NeedsReview, attempt, NeedsReviewMessage);
        }

        // The money is confirmed at the gateway; persist that before touching the bill so a
        // failure below can be retried without calling verify again.
        attempt.Status = PaymentAttemptStatus.Verified;
        await _attempts.UpdateAsync(attempt);
        setFallback(PaymentAttemptStatus.Verified);

        // 4) Settle: one transaction, exactly once.
        var settled = await _tx.ExecuteAsync(async () =>
        {
            if (!await _bills.TryMarkPaidAsync(attempt.BillId, attempt.PaidAt ?? now))
                return false;

            var payment = await _payments.AddAsync(new Payment
            {
                BillId = attempt.BillId,
                Amount = attempt.Amount,
                PaymentDate = attempt.PaidAt ?? now,
                Description = $"پرداخت آنلاین ({attempt.Gateway}) - کد پیگیری {attempt.TrackId}" +
                              (attempt.RefNumber.HasValue ? $" - شماره مرجع {attempt.RefNumber}" : string.Empty)
            });

            var bill = await _bills.GetByIdAsync(attempt.BillId);
            var house = bill is null ? null : await _houses.GetByIdAsync(bill.HouseId);
            if (house is not null)
            {
                house.CurrentDebt -= attempt.Amount;
                await _houses.UpdateAsync(house);
            }

            attempt.PaymentId = payment.Id;
            attempt.Status = PaymentAttemptStatus.Succeeded;
            attempt.Note = null;
            attempt.UpdatedAt = Now;
            await _attempts.UpdateAsync(attempt);
            return true;
        });

        if (!settled)
        {
            attempt.Status = PaymentAttemptStatus.NeedsReview;
            attempt.Note = "پرداخت تایید شد اما قبض دیگر قابل پرداخت نبود (قبلاً پرداخت شده است). مبلغ باید به پرداخت‌کننده بازگردانده شود.";
            attempt.UpdatedAt = Now;
            await _attempts.UpdateAsync(attempt);
            await AddEventAsync(attempt.Id, PaymentEventType.Warning, null, paid.Status, attempt.Note, null);
            await SafeAuditAsync(userId, userName, nameof(PaymentAttempt), attempt.Id.ToString(), "PaymentNeedsReview", null, attempt.Note);
            return Result(PaymentProcessingOutcome.NeedsReview, attempt, NeedsReviewMessage);
        }

        await AddEventAsync(attempt.Id, PaymentEventType.Settled, null, ZibalStatusCodes.PaidVerified,
            $"قبض پرداخت‌شده ثبت شد (Payment #{attempt.PaymentId})", null);
        await SafeAuditAsync(userId, userName, nameof(Payment), attempt.PaymentId?.ToString() ?? "-", "OnlinePaymentConfirmed", null,
            $"BillId={attempt.BillId}, Amount={attempt.Amount}, TrackId={attempt.TrackId}, Ref={attempt.RefNumber}");
        return Result(PaymentProcessingOutcome.Succeeded, attempt, "پرداخت با موفقیت انجام شد و قبض شما پرداخت‌شده ثبت گردید.");
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private const string NeedsReviewMessage =
        "پرداخت شما دریافت شد اما به‌صورت خودکار ثبت نشد. موضوع توسط مدیر بررسی می‌شود؛ لطفاً کد پیگیری را نزد خود نگه دارید.";

    private async Task<PaymentProcessingResult> ReleaseAsync(
        PaymentAttempt attempt, PaymentAttemptStatus fallback, string? note, PaymentProcessingOutcome outcome, string? message = null)
    {
        await _attempts.TryReleaseAsync(attempt.Id, fallback, note, Now);
        return Result(outcome, attempt, message ?? note ?? "بررسی پرداخت با خطای موقت مواجه شد.");
    }

    private async Task<PaymentProcessingResult> FinishAsync(
        PaymentAttempt attempt, PaymentAttemptStatus finalStatus, string note, PaymentProcessingOutcome outcome, GatewayTransactionInfo info)
    {
        attempt.Status = finalStatus;
        attempt.Note = note;
        attempt.ZibalStatus = info.Status ?? attempt.ZibalStatus;
        attempt.UpdatedAt = Now;
        await _attempts.UpdateAsync(attempt);
        await AddEventAsync(attempt.Id, PaymentEventType.StatusChanged, info.ResultCode, info.Status, $"{finalStatus}: {note}", null);
        return Result(outcome, attempt, note);
    }

    private static PaymentProcessingResult Result(PaymentProcessingOutcome outcome, PaymentAttempt attempt, string message) =>
        new(outcome, attempt.PublicId, message);

    private static PaymentProcessingResult Final(PaymentAttempt attempt) =>
        new(PaymentProcessingOutcome.AlreadyFinal, attempt.PublicId, DescribeFinal(attempt));

    public static string DescribeFinal(PaymentAttempt attempt) => attempt.Status switch
    {
        PaymentAttemptStatus.Succeeded => "پرداخت با موفقیت انجام و قبض پرداخت‌شده ثبت شده است.",
        PaymentAttemptStatus.Failed => attempt.Note ?? "پرداخت ناموفق بود.",
        PaymentAttemptStatus.Expired => "پرداخت در مهلت مقرر انجام نشد.",
        PaymentAttemptStatus.RequestFailed => "اتصال به درگاه برقرار نشد.",
        PaymentAttemptStatus.NeedsReview => NeedsReviewMessage,
        _ => "در حال بررسی"
    };

    public static bool IsFinal(PaymentAttemptStatus status) => status is
        PaymentAttemptStatus.Succeeded or PaymentAttemptStatus.Failed or PaymentAttemptStatus.NeedsReview
        or PaymentAttemptStatus.Expired or PaymentAttemptStatus.RequestFailed;

    /// <summary>An attempt that may still turn into a payment (has / will get a gateway session).</summary>
    public static bool IsOpen(PaymentAttemptStatus status) => status is
        PaymentAttemptStatus.Requested or PaymentAttemptStatus.CallbackReceived
        or PaymentAttemptStatus.Verifying or PaymentAttemptStatus.Verified;

    private static GatewayTransactionInfo Merge(GatewayTransactionInfo verify, GatewayTransactionInfo inquiry) => new()
    {
        ResultCode = verify.ResultCode,
        Message = verify.Message,
        Status = ZibalStatusCodes.PaidVerified,
        Amount = verify.Amount ?? inquiry.Amount,
        OrderId = verify.OrderId ?? inquiry.OrderId,
        RefNumber = verify.RefNumber ?? inquiry.RefNumber,
        CardNumber = verify.CardNumber ?? inquiry.CardNumber,
        PaidAt = verify.PaidAt ?? inquiry.PaidAt,
        VerifiedAt = verify.VerifiedAt ?? inquiry.VerifiedAt
    };

    private async Task AddEventAsync(int attemptId, PaymentEventType type, int? resultCode, int? zibalStatus, string? message, string? data)
    {
        try
        {
            await _attempts.AddEventAsync(new PaymentAttemptEvent
            {
                PaymentAttemptId = attemptId,
                CreatedAt = Now,
                EventType = type,
                ResultCode = resultCode,
                ZibalStatus = zibalStatus,
                Message = Truncate(message, 1000),
                Data = Truncate(data, MaxStoredDataLength)
            });
        }
        catch
        {
            // The trace must never break a payment.
        }
    }

    private async Task SafeAuditAsync(string userId, string userName, string entity, string entityId, string action, string? oldValues, string? newValues)
    {
        try { await _audit.LogAsync(userId, userName, entity, entityId, action, oldValues, newValues); }
        catch { /* auditing must never break a payment */ }
    }

    private static string? Truncate(string? text, int max) =>
        text is null || text.Length <= max ? text : text[..max];

    private static bool IsValidCallbackUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    /// <summary>Normalizes an Iranian mobile number to 09xxxxxxxxx, or null if it is not one.</summary>
    public static string? NormalizeMobile(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;

        var digits = new System.Text.StringBuilder();
        foreach (var c in phone)
        {
            if (c >= '0' && c <= '9') digits.Append(c);
            else if (c >= '\u06F0' && c <= '\u06F9') digits.Append((char)('0' + (c - '\u06F0')));
            else if (c >= '\u0660' && c <= '\u0669') digits.Append((char)('0' + (c - '\u0660')));
        }

        var d = digits.ToString();
        if (d.StartsWith("0098")) d = "0" + d[4..];
        else if (d.StartsWith("98") && d.Length == 12) d = "0" + d[2..];
        else if (d.Length == 10 && d.StartsWith('9')) d = "0" + d;

        return d.Length == 11 && d.StartsWith("09") ? d : null;
    }
}
