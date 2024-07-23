namespace matrix_dotnet;

public class LoginRequiredException : UnauthorizedAccessException { }

public class MatrixApiError : Exception {
	public string ErrorCode { get; }
	public string ErrorMessage { get; }
	public HttpResponseMessage Response { get; }

	public MatrixApiError(string errorCode, string errorMessage, HttpResponseMessage response, Exception? innerException)
		: base(String.Format("Request to Matrix API failed: {0}: {1}", errorCode, errorMessage), innerException) {
		ErrorCode = errorCode;
		ErrorMessage = errorMessage;
		Response = response;
	}
}

