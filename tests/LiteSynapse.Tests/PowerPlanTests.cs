using Xunit;

namespace LiteSynapse.Tests;

// The power plan row only reads the plan, and on a dual-chiplet X3D it asks who keeps games on the cache cores:
// AMD's driver (which needs core parking) or Process Lasso (which does not).
public class PowerPlanTests
{
	// A user's optimizer plan (FPSHEAVEN2026): processor minimum 100%, core parking off (100% unparked).
	private const string OptimizerPlan = "04e90473-3d30-4240-98a0-5be956cab0bf";

	[Theory]
	[InlineData("AMD Ryzen 9 7950X3D 16-Core Processor", true)]
	[InlineData("AMD Ryzen 9 7900X3D 12-Core Processor", true)]
	[InlineData("AMD Ryzen 9 9950X3D 16-Core Processor", true)]
	[InlineData("AMD Ryzen 7 7800X3D 8-Core Processor", false)]
	[InlineData("AMD Ryzen 7 9800X3D 8-Core Processor", false)]
	[InlineData("AMD Ryzen 7 5800X3D 8-Core Processor", false)]
	[InlineData("13th Gen Intel(R) Core(TM) i9-13900K", false)]
	[InlineData("", false)]
	[InlineData(null, false)]
	public void OnlyTheTwoChipletX3DPartsAreSingledOut(string cpu, bool expected)
	{
		Assert.Equal(expected, Hardware.IsDualCcdX3D(cpu));
	}

	[Fact]
	public void ProcessLassoTakesOverFromAmdsDriver()
	{
		Assert.True(Hardware.DriverParksCores(dualCcdX3D: true, lassoRunning: false));
		Assert.False(Hardware.DriverParksCores(dualCcdX3D: true, lassoRunning: true));
		Assert.False(Hardware.DriverParksCores(dualCcdX3D: false, lassoRunning: false));
		Assert.False(Hardware.DriverParksCores(dualCcdX3D: false, lassoRunning: true));
	}

	[Fact]
	public void AnOptimizerPlanReadsOnWhoeverPinsTheCores()
	{
		Assert.Equal("on", SystemScanner.PlanState(OptimizerPlan, driverParks: false, minState: 100, minCores: 100));
		Assert.Equal("on", SystemScanner.PlanState(OptimizerPlan, driverParks: true, minState: 100, minCores: 100));
		// Windows' Balanced: 5% minimum, core parking on.
		Assert.Equal("off", SystemScanner.PlanState(Hardware.BalancedPlan, driverParks: false, minState: 5, minCores: 10));
		Assert.Equal("off", SystemScanner.PlanState(Hardware.BalancedPlan, driverParks: true, minState: 5, minCores: 10));
		// A plan of its own that powercfg could not describe says nothing either way.
		Assert.Equal("other", SystemScanner.PlanState(OptimizerPlan, driverParks: false, minState: null, minCores: null));
	}

	private static TweakRow Row(string id)
	{
		Tweaks.Tweak t = Tweaks.Find(id);
		return new TweakRow { Id = t.Id, PerfOn = t.PerfOn, AppliedOn = t.PerfOn, Risk = t.Risk, What = t.What, Cost = t.Cost };
	}

	[Fact]
	public void WithProcessLassoAFullSpeedPlanIsHelpingOnAnX3D()
	{
		TweakRow plan = Row("powerplan");
		SystemScanner.ForThisPc(plan, dualCcdX3D: true, lassoRunning: true);
		Assert.True(plan.PerfOn);
		Assert.Equal(PerfModel.Helping, PerfModel.Verdict("on", plan.PerfOn, plan.Risk));
		Assert.Contains("Process Lasso", plan.What);
		// And Game DVR is an ordinary safe fix again: the driver's game detection is not what pins the game.
		TweakRow dvr = Row("gamedvr");
		string risk = dvr.Risk;
		SystemScanner.ForThisPc(dvr, dualCcdX3D: true, lassoRunning: true);
		Assert.Equal(risk, dvr.Risk);
	}

	[Fact]
	public void WithoutProcessLassoAnX3DWantsCoreParking()
	{
		TweakRow plan = Row("powerplan");
		SystemScanner.ForThisPc(plan, dualCcdX3D: true, lassoRunning: false);
		Assert.False(plan.PerfOn);
		Assert.Equal(PerfModel.Hurting, PerfModel.Verdict("on", plan.PerfOn, plan.Risk));
		Assert.Equal(PerfModel.Helping, PerfModel.Verdict("off", plan.PerfOn, plan.Risk));
		// The text never offers to change the plan.
		Assert.DoesNotContain("puts Balanced back", plan.What);
		TweakRow dvr = Row("gamedvr");
		SystemScanner.ForThisPc(dvr, dualCcdX3D: true, lassoRunning: false);
		Assert.Equal(Tweaks.Caution, dvr.Risk);
	}

	[Fact]
	public void AnyOtherCpuIsLeftAsIs()
	{
		TweakRow plan = Row("powerplan");
		SystemScanner.ForThisPc(plan, dualCcdX3D: false, lassoRunning: false);
		Assert.True(plan.PerfOn);
		Assert.DoesNotContain("X3D", plan.What);
	}

	[Fact]
	public void ThePowerPlanRowHasNoSwitchToFlip()
	{
		Tweaks.Tweak t = Tweaks.Find("powerplan");
		Assert.True(t.ReportOnly);
		Assert.False(t.NeedsAdmin);
		Assert.DoesNotContain("Balanced back", t.What + t.Cost);
	}
}
