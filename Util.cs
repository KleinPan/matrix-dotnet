using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace matrix_dotnet;

using StateDict = ImmutableDictionary<Client.StateKey, Api.EventContent>;

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


static class Util {
	public static void PrintStateDict(StateDict dict) {
		Console.WriteLine("-------");
		foreach (var kv in dict) {
			Console.WriteLine($"{kv.Key.type}/{kv.Key.state_key}: {(kv.Value is Api.RoomMember member ? member.displayname : kv.Value.GetType())}");
		}
		Console.WriteLine("-------");
	}
}
