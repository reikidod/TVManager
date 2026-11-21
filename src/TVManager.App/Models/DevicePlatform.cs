namespace TVManager.Models;

public enum DevicePlatform
{
    Unknown,
    AndroidTv,
    FireOs,
    SamsungTizen,
    LgWebOs,
    AndroidFamily
}

public enum ConnectionKind
{
    Network,
    Usb
}

public enum DetectionConfidence
{
    Low,
    Probable,
    Confirmed
}
