using System.IO;

namespace AirFlick.Protocol;

public sealed class ProtocolException : IOException
{
    public ProtocolException(ProtocolError error, string message)
        : base(message)
    {
        Error = error;
    }

    public ProtocolError Error { get; }
}
