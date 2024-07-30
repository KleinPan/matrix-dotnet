using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;

// THIS CODE IS NOT MINE
//
// Taken from: https://medium.com/@florian.baader/log-http-requests-with-refit-81ee47bffb05

public class HttpLoggingHandler : DelegatingHandler {
	ILogger Logger;
	public HttpLoggingHandler(ILogger logger, HttpMessageHandler? innerHandler = null)
		: base(innerHandler ?? new HttpClientHandler()) {
			Logger = logger;
	}

	async protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
		CancellationToken cancellationToken) {
		var req = request;

		var sb = new StringBuilder();

		sb.AppendLine($"{req.Method} {req.RequestUri?.PathAndQuery} {req.RequestUri?.Scheme}/{req.Version}");
		sb.AppendLine($"Host: {req.RequestUri?.Scheme}://{req.RequestUri?.Host}");

		if (req.Headers.Count() > 0)
			sb.AppendLine($"Headers:\n{req.Headers}");

		// foreach (var header in req.Headers)
		//	sb.AppendLine($" {header.Key}: {string.Join(", ", header.Value)}");

		if (req.Content != null) {
			if (req.Content.Headers.Count() > 0)
				sb.AppendLine($"Headers:\n{req.Content.Headers}");

			if (req.Content is StringContent || IsTextBasedContentType(req.Headers) ||
				this.IsTextBasedContentType(req.Content.Headers)) {
				var result = await req.Content.ReadAsStringAsync();

				sb.AppendLine($"Content:");
				sb.AppendLine($"{result}");
			}
		}

		var start = DateTime.Now;

		var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

		var end = DateTime.Now;

		sb.AppendLine($"\n\nDuration: {end - start}");

		Logger.LogInformation(sb.ToString());
		sb.Clear();

		var resp = response;

		sb.AppendLine($"{req.RequestUri?.Scheme.ToUpper()}/{resp.Version} {(int)resp.StatusCode} {resp.ReasonPhrase}");

		// foreach (var header in resp.Headers)
		// 	sb.AppendLine($" {header.Key}: {string.Join(", ", header.Value)}");

		if (resp.Headers.Count() > 0)
			sb.AppendLine($"Headers: {resp.Headers}");

		if (resp.Content != null) {
			if (resp.Content.Headers.Count() > 0)
				sb.AppendLine($"Headers: {resp.Content.Headers}");

			if (resp.Content is StringContent || this.IsTextBasedContentType(resp.Headers) ||
				this.IsTextBasedContentType(resp.Content.Headers)) {
				start = DateTime.Now;
				var result = await resp.Content.ReadAsStringAsync();
				end = DateTime.Now;

				sb.AppendLine($"Content:");
				sb.AppendLine($"{result}");
				sb.AppendLine($"\n\nDuration: {end - start}");
			}
		}

		Logger.LogInformation(sb.ToString());

		return response;
	}

	private object StringBuilder() {
		throw new NotImplementedException();
	}

	readonly string[] types = new[] { "html", "text", "xml", "json", "txt", "x-www-form-urlencoded" };

	bool IsTextBasedContentType(HttpHeaders headers) {
		IEnumerable<string>? values;
		if (!headers.TryGetValues("Content-Type", out values))
			return false;
		var header = string.Join(" ", values).ToLowerInvariant();

		return types.Any(t => header.Contains(t));
	}
}
