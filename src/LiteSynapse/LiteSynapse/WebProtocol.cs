using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace LiteSynapse;

// An error the window shows to the user as is (bridge reply with ok: false).
internal sealed class BridgeException : Exception
{
	public BridgeException(string message)
		: base(message)
	{
	}
}

// One JS -> C# call: { id, cmd, args } (docs/bridge-api.md). Args is a standalone copy, safe to keep.
internal readonly record struct WebRequest(int Id, string Cmd, WebArgs Args);

// Messages of the window bridge. No Windows APIs here, so the unit tests can check the JSON shapes.
internal static class WebProtocol
{
	private static readonly JsonDocumentOptions ParseOptions = new JsonDocumentOptions
	{
		MaxDepth = 32
	};

	// Parses what window.chrome.webview.postMessage sent (WebMessageAsJson). The page may also post the
	// object as a JSON string. False with an error text when it is not a request; id is -1 when unknown.
	public static bool TryParse(string json, out WebRequest request, out string error)
	{
		request = new WebRequest(-1, null, WebArgs.Empty);
		error = null;
		try
		{
			using JsonDocument doc = JsonDocument.Parse(json ?? "", ParseOptions);
			JsonElement root = doc.RootElement;
			if (root.ValueKind == JsonValueKind.String)
			{
				return TryParse(root.GetString(), out request, out error);
			}
			if (root.ValueKind != JsonValueKind.Object)
			{
				error = "message is not an object";
				return false;
			}
			if (!root.TryGetProperty("id", out JsonElement idEl) || idEl.ValueKind != JsonValueKind.Number || !idEl.TryGetInt32(out int id))
			{
				error = "message has no integer id";
				return false;
			}
			request = new WebRequest(id, null, WebArgs.Empty);
			if (!root.TryGetProperty("cmd", out JsonElement cmdEl) || cmdEl.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(cmdEl.GetString()))
			{
				error = "message has no cmd";
				return false;
			}
			WebArgs args = WebArgs.Empty;
			if (root.TryGetProperty("args", out JsonElement argsEl) && argsEl.ValueKind != JsonValueKind.Null && argsEl.ValueKind != JsonValueKind.Undefined)
			{
				if (argsEl.ValueKind != JsonValueKind.Object)
				{
					error = "args must be an object";
					return false;
				}
				args = new WebArgs(argsEl.Clone());
			}
			request = new WebRequest(id, cmdEl.GetString(), args);
			return true;
		}
		catch (JsonException ex)
		{
			error = "message is not valid JSON: " + ex.Message;
			return false;
		}
	}

	// { "type": "reply", "id": id, "ok": true, "result": result }. result must be null or a type WebJson knows.
	public static string Reply(int id, object result)
	{
		return Write(w =>
		{
			w.WriteString("type", "reply");
			w.WriteNumber("id", id);
			w.WriteBoolean("ok", true);
			w.WritePropertyName("result");
			WriteValue(w, result);
		});
	}

	// { "type": "reply", "id": id, "ok": false, "error": message }
	public static string Error(int id, string message)
	{
		return Write(w =>
		{
			w.WriteString("type", "reply");
			w.WriteNumber("id", id);
			w.WriteBoolean("ok", false);
			w.WriteString("error", string.IsNullOrWhiteSpace(message) ? "Something went wrong." : message);
		});
	}

	// { "type": "state", "state": state }
	public static string State(StateDto state)
	{
		return Write(w =>
		{
			w.WriteString("type", "state");
			w.WritePropertyName("state");
			WriteValue(w, state);
		});
	}

	// { "type": "progress", "op": op, "text": text }
	public static string Progress(string op, string text)
	{
		return Write(w =>
		{
			w.WriteString("type", "progress");
			w.WriteString("op", op);
			w.WriteString("text", text ?? "");
		});
	}

	// { "type": "navigate", "page": page, "tab": tab } (tab is left out when null).
	public static string Navigate(string page, string tab)
	{
		return Write(w =>
		{
			w.WriteString("type", "navigate");
			w.WriteString("page", page);
			if (tab != null)
			{
				w.WriteString("tab", tab);
			}
		});
	}

	private static void WriteValue(Utf8JsonWriter w, object value)
	{
		if (value == null)
		{
			w.WriteNullValue();
			return;
		}
		JsonTypeInfo info = WebJson.Default.GetTypeInfo(value.GetType());
		if (info == null)
		{
			throw new NotSupportedException("bridge cannot send a " + value.GetType().Name);
		}
		JsonSerializer.Serialize(w, value, info);
	}

	private static string Write(Action<Utf8JsonWriter> body)
	{
		using MemoryStream ms = new MemoryStream();
		using (Utf8JsonWriter w = new Utf8JsonWriter(ms))
		{
			w.WriteStartObject();
			body(w);
			w.WriteEndObject();
		}
		return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
	}
}

// Typed, validating access to a command's args object. Every getter throws BridgeException with a
// message the user can read when the value is missing or wrong.
internal readonly struct WebArgs
{
	public static readonly WebArgs Empty = Parse("{}");

	private readonly JsonElement root;

	public WebArgs(JsonElement root)
	{
		this.root = root;
	}

	public static WebArgs Parse(string json)
	{
		using JsonDocument doc = JsonDocument.Parse(json);
		return new WebArgs(doc.RootElement.Clone());
	}

	public bool Has(string name)
	{
		return root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out JsonElement v) && v.ValueKind != JsonValueKind.Null && v.ValueKind != JsonValueKind.Undefined;
	}

	private JsonElement Get(string name)
	{
		if (!Has(name))
		{
			throw new BridgeException($"\"{name}\" is missing.");
		}
		return root.GetProperty(name);
	}

	// A string (may be empty). Null only when optional and missing.
	public string Str(string name, bool optional = false)
	{
		if (!Has(name))
		{
			if (optional)
			{
				return null;
			}
			throw new BridgeException($"\"{name}\" is missing.");
		}
		JsonElement v = root.GetProperty(name);
		if (v.ValueKind != JsonValueKind.String)
		{
			throw new BridgeException($"\"{name}\" must be text.");
		}
		return v.GetString();
	}

	public bool Bool(string name)
	{
		JsonElement v = Get(name);
		switch (v.ValueKind)
		{
		case JsonValueKind.True:
			return true;
		case JsonValueKind.False:
			return false;
		case JsonValueKind.Number when v.TryGetDouble(out double d) && (d == 0 || d == 1):
			return d == 1;
		default:
			throw new BridgeException($"\"{name}\" must be true or false.");
		}
	}

	// A whole number in min..max. Numbers with a fraction are rounded (sliders may send 42.5).
	public int Int(string name, int min, int max)
	{
		return ToInt(Get(name), name, min, max);
	}

	public int Int(string name, int min, int max, int fallback)
	{
		return Has(name) ? Int(name, min, max) : fallback;
	}

	public List<int> Ints(string name, int min, int max)
	{
		JsonElement v = Get(name);
		if (v.ValueKind != JsonValueKind.Array)
		{
			throw new BridgeException($"\"{name}\" must be a list of numbers.");
		}
		List<int> list = new List<int>();
		foreach (JsonElement e in v.EnumerateArray())
		{
			list.Add(ToInt(e, name, min, max));
		}
		return list;
	}

	// A list of strings; null entries are skipped.
	public List<string> Strings(string name, bool optional = false)
	{
		if (!Has(name))
		{
			if (optional)
			{
				return new List<string>();
			}
			throw new BridgeException($"\"{name}\" is missing.");
		}
		JsonElement v = root.GetProperty(name);
		if (v.ValueKind != JsonValueKind.Array)
		{
			throw new BridgeException($"\"{name}\" must be a list of text values.");
		}
		List<string> list = new List<string>();
		foreach (JsonElement e in v.EnumerateArray())
		{
			if (e.ValueKind == JsonValueKind.Null)
			{
				continue;
			}
			if (e.ValueKind != JsonValueKind.String)
			{
				throw new BridgeException($"\"{name}\" must be a list of text values.");
			}
			list.Add(e.GetString());
		}
		return list;
	}

	// A nested object.
	public WebArgs Obj(string name)
	{
		JsonElement v = Get(name);
		if (v.ValueKind != JsonValueKind.Object)
		{
			throw new BridgeException($"\"{name}\" must be an object.");
		}
		return new WebArgs(v);
	}

	// Each element of an array of objects.
	public List<WebArgs> Objects(string name)
	{
		JsonElement v = Get(name);
		if (v.ValueKind != JsonValueKind.Array)
		{
			throw new BridgeException($"\"{name}\" must be a list.");
		}
		List<WebArgs> list = new List<WebArgs>();
		foreach (JsonElement e in v.EnumerateArray())
		{
			if (e.ValueKind != JsonValueKind.Object)
			{
				throw new BridgeException($"\"{name}\" must be a list of objects.");
			}
			list.Add(new WebArgs(e));
		}
		return list;
	}

	// "mouse" or "keyboard" (args.kind).
	public bool IsMouseKind()
	{
		string kind = Str("kind");
		return kind switch
		{
			"mouse" => true,
			"keyboard" => false,
			_ => throw new BridgeException("\"kind\" must be \"mouse\" or \"keyboard\"."),
		};
	}

	private static int ToInt(JsonElement v, string name, int min, int max)
	{
		double d;
		if (v.ValueKind == JsonValueKind.Number)
		{
			d = v.GetDouble();
		}
		else if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
		{
			d = parsed;
		}
		else
		{
			throw new BridgeException($"\"{name}\" must be a number.");
		}
		if (double.IsNaN(d) || double.IsInfinity(d))
		{
			throw new BridgeException($"\"{name}\" must be a number.");
		}
		double r = Math.Round(d, MidpointRounding.AwayFromZero);
		if (r < min || r > max)
		{
			throw new BridgeException($"\"{name}\" must be between {min} and {max}.");
		}
		return (int)r;
	}
}
