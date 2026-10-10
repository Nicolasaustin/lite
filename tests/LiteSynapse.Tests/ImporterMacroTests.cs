using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;

namespace LiteSynapse.Tests;

// Importing macros from files for the macro page: Razer Synapse 4 (.synapse4, can hold several), a LiteSynapse
// export (.json), and the error when a file has no macros.
public class ImporterMacroTests : IDisposable
{
	private readonly string dir;

	public ImporterMacroTests()
	{
		dir = Path.Combine(Path.GetTempPath(), "litesynapse-import-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
	}

	public void Dispose()
	{
		try { Directory.Delete(dir, recursive: true); } catch { }
	}

	private string Write(string name, string text)
	{
		string path = Path.Combine(dir, name);
		File.WriteAllText(path, text);
		return path;
	}

	// A Synapse 4 macro payload: press W, wait 50 ms, release W.
	private static string MacroPayload()
	{
		string json = "{\"macroEvents\":[" +
			"{\"Type\":\"1\",\"KeyEvent\":{\"Makecode\":87,\"IsExtended\":\"false\",\"State\":0}}," +
			"{\"Type\":\"0\",\"Number\":0.05}," +
			"{\"Type\":\"1\",\"KeyEvent\":{\"Makecode\":87,\"IsExtended\":\"false\",\"State\":1}}" +
			"]}";
		return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
	}

	[Fact]
	public void ImportsMacrosFromASynapse4Export()
	{
		string file = Write("export.synapse4",
			"{\"macros\":[{\"name\":\"W Jumpthrow\",\"payload\":\"" + MacroPayload() + "\"}," +
			"{\"name\":\"Jumpthrow\",\"payload\":\"" + MacroPayload() + "\"}]}");
		List<Macro> macros = Importer.ImportMacrosFromFile(file);
		Assert.Equal(2, macros.Count);
		Assert.Equal("W Jumpthrow", macros[0].Name);
		Assert.Equal(3, macros[0].Steps.Count);
		Assert.True(macros[0].Steps[0].Down);
		Assert.Equal(50, macros[0].Steps[1].DelayMs);
		Assert.False(macros[0].Steps[2].Down);
	}

	[Fact]
	public void ASynapse4FileWithNoMacrosSaysSo()
	{
		string file = Write("profiles-only.synapse4", "{\"category\":\"MOUSE\",\"profiles\":[]}");
		InvalidDataException ex = Assert.Throws<InvalidDataException>(() => Importer.ImportMacrosFromFile(file));
		Assert.Contains("no macros", ex.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void ImportsALiteSynapseJsonExport()
	{
		Macro m = new Macro { Name = "Tarky middle" };
		m.Steps.Add(MacroEdit.MouseStep(MacroEdit.MouseMiddle, down: true, delayMs: 5));
		m.Steps.Add(MacroEdit.MouseStep(MacroEdit.MouseMiddle, down: false));
		string file = Write("mine.json", MacroEdit.ToJson(m));
		List<Macro> macros = Importer.ImportMacrosFromFile(file);
		Assert.Single(macros);
		Assert.Equal("Tarky middle", macros[0].Name);
		Assert.Equal(MacroEdit.MouseMiddle, macros[0].Steps[0].Mouse);
	}
}
