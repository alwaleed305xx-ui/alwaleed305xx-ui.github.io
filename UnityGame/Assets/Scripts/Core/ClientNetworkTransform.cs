using Unity.Netcode.Components;

/// <summary>
/// NetworkTransform بصلاحية المالك — اللاعب يحرك شخصيته بنفسه والباقين يشوفونه.
/// (النسخة الافتراضية صلاحيتها للسيرفر فقط، فنحتاج هذا للاعبين)
/// </summary>
public class ClientNetworkTransform : NetworkTransform
{
    protected override bool OnIsServerAuthoritative() => false;
}
