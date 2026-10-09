using System;
using System.Collections.Generic;

namespace LiteSynapse;

// Test double for the app's RazerDevice, which talks to the device through Windows HID APIs.
// It has the members the linked app code calls, records every report, and answers from scripts set by a test.
internal class RazerDevice
{
	public readonly List<(byte Txn, byte Class, byte Id, byte Size, byte[] Args)> Sent = new List<(byte, byte, byte, byte, byte[])>();

	// Status Raw returns for a command (2 = success).
	public Func<byte, byte, byte[], int> RawStatus = (byte cls, byte id, byte[] args) => 2;

	// Reply Command returns for a command (null = the device did not answer with success).
	public Func<byte, byte, byte[], byte[]> CommandReply = (byte cls, byte id, byte[] args) => new byte[80];

	public bool SetBrightnessResult = true;

	public readonly List<int> BrightnessWrites = new List<int>();

	public int? Brightness;

	public string Name { get; } = "Fake";

	public int Raw(byte txnId, byte cls, byte id, byte size, byte[] args)
	{
		Sent.Add((txnId, cls, id, size, (byte[])args.Clone()));
		return RawStatus(cls, id, args);
	}

	public byte[] Command(byte cls, byte id, byte size, params byte[] args)
	{
		Sent.Add((0, cls, id, size, (byte[])args.Clone()));
		return CommandReply(cls, id, args);
	}

	public const int StatusOk = 2;

	// Status-returning command: 2 when CommandReply answers, 3 (failed) when it returns null.
	public int Send(byte cls, byte id, byte size, byte[] args, byte[] reply = null)
	{
		Sent.Add((0, cls, id, size, (byte[])args.Clone()));
		byte[] answer = CommandReply(cls, id, args);
		if (answer == null)
		{
			return 3;
		}
		if (reply != null)
		{
			Array.Copy(answer, reply, Math.Min(answer.Length, reply.Length));
		}
		return StatusOk;
	}

	public int? GetBrightness()
	{
		return Brightness;
	}

	public bool SetBrightness(int percent)
	{
		BrightnessWrites.Add(percent);
		if (SetBrightnessResult)
		{
			Brightness = percent;
		}
		return SetBrightnessResult;
	}

	// Onboard slots (used by LiveProfile). ActiveSlot null = the mouse does not answer.
	public int? ActiveSlot = 3;

	public readonly List<int> SlotSwitches = new List<int>();

	public readonly List<int> DpiWrites = new List<int>();

	public readonly List<int> PollingWrites = new List<int>();

	public int? GetActiveSlot()
	{
		return ActiveSlot;
	}

	public bool SetActiveSlot(int slot)
	{
		SlotSwitches.Add(slot);
		ActiveSlot = slot;
		return true;
	}

	public bool SetDpi(int dpi, int? slot = null)
	{
		DpiWrites.Add(dpi);
		return true;
	}

	public bool SetPolling(int hz)
	{
		PollingWrites.Add(hz);
		return true;
	}
}
