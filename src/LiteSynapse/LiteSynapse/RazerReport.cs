using System;

namespace LiteSynapse;

// Builds a protocol 2.5 feature report: [0] report id 0, [1] status, [2] transaction id,
// [3-4] remaining packets, [5] protocol type, [6] data size, [7] class, [8] command, [9-88] arguments,
// [89] CRC = XOR of bytes 3..88, [90] reserved.
internal static class RazerReport
{
	public const int Length = 91;

	public const int ArgsOffset = 9;

	public const int ArgsLength = 80;

	public static void Build(byte[] r, byte txnId, byte cls, byte id, byte size, ReadOnlySpan<byte> args)
	{
		Array.Clear(r);
		r[2] = txnId;
		r[6] = size;
		r[7] = cls;
		r[8] = id;
		args.Slice(0, Math.Min(args.Length, ArgsLength)).CopyTo(r.AsSpan(ArgsOffset));
		r[89] = Crc(r);
	}

	// True when a status reply is the answer to this request: it repeats the request's class and command.
	public static bool Answers(byte[] request, byte[] response)
	{
		return response[7] == request[7] && response[8] == request[8];
	}

	public static byte Crc(byte[] r)
	{
		byte crc = 0;
		for (int i = 3; i < 89; i++)
		{
			crc ^= r[i];
		}
		return crc;
	}
}

// When to read a command's status again. The SET is sent once; after it the status is read
// (Synapse's host: first read ~5 ms after the SET, 3 ms more per retry; OpenRazer: on "busy" ask again,
// never re-send). A device that keeps saying "busy" (a flash write) is given up to about a second more.
internal static class StatusPoll
{
	public const int New = 0;

	public const int Busy = 1;

	public const int Ok = 2;

	public const int Failed = 3;

	public const int Timeout = 4;

	public const int NotSupported = 5;

	public const int FirstWaitMs = 5;

	public const int StepMs = 3;

	// Quick reads while the device says "new" or "busy": 5, 8, 11, 14, 17 ms apart (55 ms in all).
	public const int QuickReads = 5;

	// Further reads only while it says "busy", 25 ms apart.
	public const int BusyReads = 40;

	public const int BusyWaitMs = 25;

	// True for an answer that ends the transaction (anything but "new" and "busy").
	public static bool IsFinal(int status)
	{
		return status != New && status != Busy;
	}

	// Milliseconds to wait before status read number 'read' (0 = the first read after the SET), given what the
	// previous read said; -1 = stop asking and keep the last status.
	public static int WaitBefore(int read, int lastStatus)
	{
		if (read <= 0)
		{
			return FirstWaitMs;
		}
		if (IsFinal(lastStatus))
		{
			return -1;
		}
		if (read < QuickReads)
		{
			return FirstWaitMs + StepMs * read;
		}
		if (lastStatus == Busy && read < QuickReads + BusyReads)
		{
			return BusyWaitMs;
		}
		return -1;
	}
}
