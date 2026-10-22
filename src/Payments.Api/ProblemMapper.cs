using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Payments.Domain;

namespace Payments.Api
{
    public static class ProblemMapper
    {
        public static IResult ToProblem(PaymentError error, HttpContext http)
        {
            var (status, body) = Refusal(error, http);

            return Results.Content(body, "application/problem+json", statusCode: status);
        }

        /// <summary>
        /// A domain refusal as a status and the exact body string. A 409 or a 422 from the state
        /// machine is a final business answer, so it gets stored against the Idempotency-Key and
        /// replayed later. Storing an answer needs it as text, and the stored text must be the
        /// same bytes the first caller saw — so the body is built once, here, and nowhere else.
        /// </summary>
        public static (int Status, string Body) Refusal(PaymentError error, HttpContext http)
        {
            var code = Code(error.Code);

            var problem = new ProblemDetails
            {
                Type = $"urn:payments-labs:error:{code}",
                Title = Title(error.Code),
                Detail = error.Message,
                Status = Status(error.Code),
                Instance = http.Request.Path
            };

            // Extensions is the [JsonExtensionData] bag, so "code" lands at the top level,
            // exactly where Results.Problem used to put it.
            problem.Extensions["code"] = code;

            return (problem.Status!.Value, JsonSerializer.Serialize(problem, JsonSerializerOptions.Web));
        }

        public static IResult NotFound(string code, string detail, HttpContext http) =>
            Results.Problem(
                type: $"urn:payments-labs:error:{code}",
                title: code == "merchant_not_found"
                    ? "There is no merchant with that id."
                    : "There is no payment with that id.",
                detail: detail,
                statusCode: StatusCodes.Status404NotFound,
                instance: http.Request.Path,
                extensions: new Dictionary<string, object?> { ["code"] = code }
            );

        /// <summary>A 400 for a query parameter the caller can fix. Carries the "code" the client reads.</summary>
        public static IResult BadRequest(string code, string detail, HttpContext http) =>
            Results.Problem(
                type: $"urn:payments-labs:error:{code}",
                title: BadRequestTitle(code),
                detail: detail,
                statusCode: StatusCodes.Status400BadRequest,
                instance: http.Request.Path,
                extensions: new Dictionary<string, object?> { ["code"] = code }
            );

        private static string BadRequestTitle(string code) => code switch
        {
            "limit_out_of_range" => "The limit must be between 1 and 20.",
            "invalid_status" => "That is not a payment status.",
            _ => "The request was not valid."
        };

        /// <summary>
        /// A problem about the Idempotency-Key itself. Same URN and same "code" extension as every
        /// other problem, so a client reads all our errors the same way. The 409 also says when to
        /// try again: the other request is still running, and one second is long enough to matter.
        /// </summary>
        public static IResult Idempotency(string code, int status, string detail, HttpContext http)
        {
            if (code == "idempotency_request_in_progress")
                http.Response.Headers.RetryAfter = "1";

            return Results.Problem(
                type: $"urn:payments-labs:error:{code}",
                title: IdempotencyTitle(code),
                detail: detail,
                statusCode: status,
                instance: http.Request.Path,
                extensions: new Dictionary<string, object?> { ["code"] = code });
        }

        private static string IdempotencyTitle(string code) => code switch
        {
            "idempotency_key_required" => "An Idempotency-Key header is required.",
            "idempotency_key_invalid" => "The Idempotency-Key is not valid.",
            "idempotency_key_reused" => "That Idempotency-Key was used for a different request.",
            "idempotency_request_in_progress" => "The first request with that key is still running.",
            _ => "The request was refused."
        };

        public static IResult Auth(int status, string code, string detail, HttpContext http,
            (string Key, object? Value)? extra = null)
        {
            var extensions = new Dictionary<string, object?> { ["code"] = code };
            if (extra is { } e) extensions[e.Key] = e.Value;

            return Results.Problem(
                type: $"urn:payments-labs:error:{code}",
                title: AuthTitle(code),
                detail: detail,
                statusCode: status,
                instance: http.Request.Path,
                extensions: extensions);
        }

        private static string AuthTitle(string code) => code switch
        {
            "api_key_required" => "An API key is required.",
            "api_key_invalid" => "The API key is not valid.",
            "insufficient_scope" => "The API key does not have the scope for this call.",
            "merchant_mismatch" => "The API key belongs to another merchant.",
            _ => "The request was refused."
        };

        static string Code(PaymentErrorCode c) => c switch
        {
            PaymentErrorCode.IllegalTransition => "illegal_transition",
            PaymentErrorCode.AmountNotPositive => "amount_not_positive",
            PaymentErrorCode.AmountExceedsAuthorised => "amount_exceeds_authorised",
            PaymentErrorCode.AmountExceedsCaptured => "amount_exceeds_captured",
            PaymentErrorCode.CurrencyMismatch => "currency_mismatch"
        };

        private static int Status(PaymentErrorCode c) => c switch
        {
            PaymentErrorCode.IllegalTransition => StatusCodes.Status409Conflict,
            PaymentErrorCode.AmountNotPositive => StatusCodes.Status422UnprocessableEntity,
            PaymentErrorCode.AmountExceedsAuthorised => StatusCodes.Status422UnprocessableEntity,
            PaymentErrorCode.AmountExceedsCaptured => StatusCodes.Status422UnprocessableEntity,
            PaymentErrorCode.CurrencyMismatch => StatusCodes.Status422UnprocessableEntity
        };

        private static string Title(PaymentErrorCode c) => c switch
        {
            PaymentErrorCode.IllegalTransition => "That action is not allowed in the current state.",
            PaymentErrorCode.AmountNotPositive => "The amount must be greater than zero.",
            PaymentErrorCode.AmountExceedsAuthorised => "The capture is larger than what is left to capture.",
            PaymentErrorCode.AmountExceedsCaptured => "The refund is larger than what is left to refund.",
            PaymentErrorCode.CurrencyMismatch => "The currency does not match the payment."
        };

    }
}