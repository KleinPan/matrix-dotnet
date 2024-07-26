using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace matrix_dotnet;

class Retry {
	public class RetryException : Exception { }

	public static async Task<TResult> RetryAsync<TResult>(Func<Task<TResult>> func) {
	retry:
		try {
			return await func();
		} catch (RetryException) {
			goto retry;
		}
	}
}

