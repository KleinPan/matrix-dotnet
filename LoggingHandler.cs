using System.Diagnostics;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

// THIS CODE IS NOT MINE
//
// Taken from: https://medium.com/@florian.baader/log-http-requests-with-refit-81ee47bffb05

public class HttpLoggingHandler : DelegatingHandler {
	ILogger Logger;
	public HttpLoggingHandler(ILogger logger, HttpMessageHandler innerHandler = null)
		: base(innerHandler ?? new HttpClientHandler()) {
			Logger = logger;
	}

	async protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
		CancellationToken cancellationToken) {
		var req = request;
		var id = Guid.NewGuid().ToString();
		var msg = $"[{id} -   Request]";

		Logger.LogInformation($"{msg}========Start==========");
		Logger.LogInformation($"{msg} {req.Method} {req.RequestUri.PathAndQuery} {req.RequestUri.Scheme}/{req.Version}");
		Logger.LogInformation($"{msg} Host: {req.RequestUri.Scheme}://{req.RequestUri.Host}");

		foreach (var header in req.Headers)
			Logger.LogInformation($"{msg} {header.Key}: {string.Join(", ", header.Value)}");

		if (req.Content != null) {
			foreach (var header in req.Content.Headers)
				Logger.LogInformation($"{msg} {header.Key}: {string.Join(", ", header.Value)}");

			if (req.Content is StringContent || IsTextBasedContentType(req.Headers) ||
				this.IsTextBasedContentType(req.Content.Headers)) {
				var result = await req.Content.ReadAsStringAsync();

				Logger.LogInformation($"{msg} Content:");
				Logger.LogInformation($"{msg} {result}");
			}
		}

		var start = DateTime.Now;

		var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

		var end = DateTime.Now;

		Logger.LogInformation($"{msg} Duration: {end - start}");
		Logger.LogInformation($"{msg}==========End==========");

		msg = $"[{id} - Response]";
		Logger.LogInformation($"{msg}=========Start=========");

		var resp = response;

		Logger.LogInformation(
			$"{msg} {req.RequestUri.Scheme.ToUpper()}/{resp.Version} {(int)resp.StatusCode} {resp.ReasonPhrase}");

		foreach (var header in resp.Headers)
			Logger.LogInformation($"{msg} {header.Key}: {string.Join(", ", header.Value)}");

		if (resp.Content != null) {
			foreach (var header in resp.Content.Headers)
				Logger.LogInformation($"{msg} {header.Key}: {string.Join(", ", header.Value)}");

			if (resp.Content is StringContent || this.IsTextBasedContentType(resp.Headers) ||
				this.IsTextBasedContentType(resp.Content.Headers)) {
				start = DateTime.Now;
				var result = await resp.Content.ReadAsStringAsync();
				end = DateTime.Now;

				Logger.LogInformation($"{msg} Content:");
				Logger.LogInformation($"{msg} {result}");
				Logger.LogInformation($"{msg} Duration: {end - start}");
			}
		}

		Logger.LogInformation($"{msg}==========End==========");
		return response;
	}

	readonly string[] types = new[] { "html", "text", "xml", "json", "txt", "x-www-form-urlencoded" };

	bool IsTextBasedContentType(HttpHeaders headers) {
		IEnumerable<string> values;
		if (!headers.TryGetValues("Content-Type", out values))
			return false;
		var header = string.Join(" ", values).ToLowerInvariant();

		return types.Any(t => header.Contains(t));
	}
}
