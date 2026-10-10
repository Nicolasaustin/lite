using System;

namespace LiteSynapse;

// A request the user made that cannot be carried out, with a message written for them ("That name is already
// taken."). ProfileOps throws it; the window shows the message and nothing else happens.
internal sealed class OpError : Exception
{
	public OpError(string message)
		: base(message)
	{
	}
}
