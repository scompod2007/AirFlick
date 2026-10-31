namespace AirFlick.Protocol;

public enum ProtocolError
{
    PartialRead,
    MalformedMagic,
    UnsupportedVersion,
    ProtocolMismatch,
    InvalidHeaderSize,
    InvalidFrameType,
    InvalidChannel,
    InvalidLength,
    MetadataTooLarge,
    PayloadTooLarge
}
