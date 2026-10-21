using Payments.Domain;

namespace Payments.Api
{
    public static class ProblemMapper
    {
         public static IResult ToProblem(PaymentError error, HttpContext http)
        {
            var code = Code(error.Code);

            return Results.Problem(
                type: $"urn:payments-labs:error:{code}",
                title: Title(error.Code),
                detail: error.Message,
                statusCode: Status(error.Code),
                instance: http.Request.Path,
                extensions: new Dictionary<string, object?> { ["code"] = code });
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