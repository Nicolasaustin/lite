using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace LiteSynapse.Tests;

public class RazerLeftoversTests
{
	[Theory]
	[InlineData(false, 2, 4, "absent")]
	[InlineData(true, 2, 4, "running")]
	[InlineData(true, 4, 4, "running")]
	[InlineData(true, 2, 1, "stopped")]
	[InlineData(true, 3, 1, "stopped")]
	[InlineData(true, 4, 1, "disabled")]
	[InlineData(true, 4, 0, "disabled")]
	[InlineData(true, 2, 2, "running")]
	public void ServiceState(bool exists, int start, int state, string expected)
	{
		Assert.Equal(expected, RazerLeftovers.ServiceState(exists, start, state));
	}

	[Fact]
	public void FiltersAreNamedForTheUser()
	{
		List<string> labels = RazerLeftovers.FilterLabels(new[] { "RzDev_008d", "mouclass", "rzdev_0235", "mshidkmdf", null }).ToList();
		Assert.Equal(new List<string> { "RzDev_008d (Naga)", "RzDev_0235 (BlackWidow Lite)" }, labels);
		Assert.Empty(RazerLeftovers.FilterLabels(null));
	}

	[Theory]
	[InlineData("Razer Synapse", true)]
	[InlineData("Razer Synapse 4", true)]
	[InlineData("RazerAppEngine", true)]
	[InlineData("Razer App Engine", true)]
	[InlineData("Razer Cortex", false)]
	[InlineData("LiteSynapse", false)]
	[InlineData("", false)]
	[InlineData(null, false)]
	public void SynapseUninstallEntries(string name, bool expected)
	{
		Assert.Equal(expected, RazerLeftovers.IsSynapseEntry(name));
	}

	[Fact]
	public void MultiSzIsSplit()
	{
		byte[] data = Encoding.Unicode.GetBytes("RzDev_008d\0kbdhid\0\0");
		Assert.Equal(new List<string> { "RzDev_008d", "kbdhid" }, RazerLeftovers.ParseMultiSz(data, data.Length));
		Assert.Empty(RazerLeftovers.ParseMultiSz(null, 0));
	}

	[Fact]
	public void DefaultsMeanNotCheckedAndNothingFound()
	{
		RazerLeftovers r = new RazerLeftovers();
		Assert.False(r.Checked);
		Assert.Equal("absent", r.GmsService);
		Assert.Empty(r.FilterDrivers);
		Assert.Equal("not checked", r.ToString());
	}
}
