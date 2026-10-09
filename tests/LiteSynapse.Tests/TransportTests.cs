using System;
using System.Collections.Generic;
using Xunit;

namespace LiteSynapse.Tests;

public class TransportTests
{
	[Fact]
	public void AReplyAnswersTheRequestWithTheSameClassAndCommand()
	{
		byte[] req = new byte[RazerReport.Length];
		RazerReport.Build(req, 0x1F, 0x05, 0x84, 1, new byte[0]);
		byte[] reply = (byte[])req.Clone();
		reply[1] = 2;
		reply[9] = 3;
		Assert.True(RazerReport.Answers(req, reply));
		// Another program's GET (lighting) answered in between: not ours.
		byte[] other = new byte[RazerReport.Length];
		RazerReport.Build(other, 0x1F, 0x0F, 0x82, 3, new byte[] { 1, 1, 0 });
		other[1] = 2;
		Assert.False(RazerReport.Answers(req, other));
		other[7] = 0x05;
		Assert.False(RazerReport.Answers(req, other));
	}

	[Fact]
	public void ReportLayoutAndCrc()
	{
		byte[] r = new byte[RazerReport.Length];
		RazerReport.Build(r, 0x1F, 0x04, 0x05, 7, new byte[] { 1, 0x03, 0x20, 0x03, 0x20 });
		Assert.Equal(0, r[0]);
		Assert.Equal(0, r[1]);
		Assert.Equal(0x1F, r[2]);
		Assert.Equal(7, r[6]);
		Assert.Equal(0x04, r[7]);
		Assert.Equal(0x05, r[8]);
		Assert.Equal(new byte[] { 1, 0x03, 0x20, 0x03, 0x20 }, r[9..14]);
		byte crc = 0;
		for (int i = 3; i < 89; i++)
		{
			crc ^= r[i];
		}
		Assert.Equal(crc, r[89]);
		Assert.Equal(0, r[90]);
	}

	[Fact]
	public void BuildClearsThePreviousReport()
	{
		byte[] r = new byte[RazerReport.Length];
		Array.Fill(r, (byte)0xAA);
		RazerReport.Build(r, 0x3F, 0x0F, 0x84, 3, new byte[] { 1, 5 });
		Assert.Equal(0, r[1]);
		Assert.Equal(0, r[11]);
		Assert.Equal(0, r[90]);
		Assert.Equal(RazerReport.Crc(r), r[89]);
	}

	[Fact]
	public void ArgumentsAreCappedAt80Bytes()
	{
		byte[] r = new byte[RazerReport.Length];
		byte[] args = new byte[100];
		Array.Fill(args, (byte)7);
		RazerReport.Build(r, 0x1F, 0x02, 0x0C, 80, args);
		Assert.Equal(7, r[88]);
		Assert.Equal(RazerReport.Crc(r), r[89]);
	}

	// The waits between status reads for a device that answers with the given statuses; the SET is sent once.
	private static List<int> Schedule(params int[] answers)
	{
		List<int> waits = new List<int>();
		int status = StatusPoll.New;
		for (int read = 0; ; read++)
		{
			int wait = StatusPoll.WaitBefore(read, status);
			if (wait < 0)
			{
				return waits;
			}
			waits.Add(wait);
			status = (read < answers.Length) ? answers[read] : answers[^1];
		}
	}

	[Fact]
	public void SuccessOnTheFirstReadTakesFiveMilliseconds()
	{
		Assert.Equal(new List<int> { 5 }, Schedule(StatusPoll.Ok));
	}

	[Theory]
	[InlineData(StatusPoll.Failed)]
	[InlineData(StatusPoll.Timeout)]
	[InlineData(StatusPoll.NotSupported)]
	public void FailureStopsAtOnce(int status)
	{
		Assert.Equal(new List<int> { 5 }, Schedule(status));
	}

	[Fact]
	public void NewOrBusyIsAskedAgainWithGrowingWaits()
	{
		Assert.Equal(new List<int> { 5, 8, 11 }, Schedule(StatusPoll.New, StatusPoll.Busy, StatusPoll.Ok));
	}

	[Fact]
	public void ANewStatusIsGivenUpAfterFiveQuickReads()
	{
		Assert.Equal(new List<int> { 5, 8, 11, 14, 17 }, Schedule(StatusPoll.New));
	}

	[Fact]
	public void AFlashWriteThatStaysBusyGetsAboutASecondMore()
	{
		List<int> waits = Schedule(StatusPoll.Busy);
		Assert.Equal(StatusPoll.QuickReads + StatusPoll.BusyReads, waits.Count);
		Assert.Equal(new List<int> { 5, 8, 11, 14, 17 }, waits.GetRange(0, 5));
		Assert.All(waits.GetRange(5, waits.Count - 5), (int w) => Assert.Equal(StatusPoll.BusyWaitMs, w));
		int total = 0;
		waits.ForEach((int w) => total += w);
		Assert.InRange(total, 1000, 1100);
	}

	[Fact]
	public void BusyThenSuccessEndsTheExtendedPolling()
	{
		int[] answers = new int[8] { 1, 1, 1, 1, 1, 1, 1, 2 };
		List<int> waits = Schedule(answers);
		Assert.Equal(8, waits.Count);
		Assert.Equal(25, waits[^1]);
	}

	[Fact]
	public void StatusFinality()
	{
		Assert.False(StatusPoll.IsFinal(StatusPoll.New));
		Assert.False(StatusPoll.IsFinal(StatusPoll.Busy));
		Assert.True(StatusPoll.IsFinal(StatusPoll.Ok));
		Assert.True(StatusPoll.IsFinal(StatusPoll.Failed));
	}
}
