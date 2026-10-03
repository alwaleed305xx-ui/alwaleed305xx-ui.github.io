using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// شاشة الاتصال: استضف لعبة أو ادخل على ربعك، وزر "ابدأ الجولة" للمضيف.
/// تختفي تلقائياً أول ما تبدأ الجولة.
/// (للعب أونلاين عبر الإنترنت استخدم ويدجت Multiplayer Session بدالها —
/// هذي للتجربة السريعة على نفس الجهاز أو الشبكة المحلية)
/// </summary>
public class ConnectUI : MonoBehaviour
{
    public GameObject panel;
    public InputField addressInput;
    public Button hostButton;
    public Button clientButton;
    public Button startButton;
    public Text statusText;

    void Start()
    {
        hostButton.onClick.AddListener(Host);
        clientButton.onClick.AddListener(Join);
        startButton.onClick.AddListener(StartRound);
        startButton.gameObject.SetActive(false);
        Cursor.lockState = CursorLockMode.None;
    }

    void Host()
    {
        SetAddress(true);
        if (NetworkManager.Singleton.StartHost())
        {
            statusText.text = ArabicText.Fix("أنت المضيف! خل الربع يدخلون على عنوانك، وإذا تجمعوا اضغط ابدأ");
            startButton.gameObject.SetActive(true);
            ToggleJoinControls(false);
        }
        else statusText.text = ArabicText.Fix("فشلت الاستضافة 😬 جرب مرة ثانية");
    }

    void Join()
    {
        SetAddress(false);
        if (NetworkManager.Singleton.StartClient())
        {
            statusText.text = ArabicText.Fix("جاري الاتصال بالمضيف...");
            ToggleJoinControls(false);
        }
        else statusText.text = ArabicText.Fix("فشل الاتصال 😬 تأكد من العنوان");
    }

    void SetAddress(bool isHost)
    {
        var utp = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (utp == null) return;
        string addr = addressInput != null ? addressInput.text.Trim() : "";
        if (string.IsNullOrEmpty(addr)) addr = "127.0.0.1";
        if (isHost) utp.SetConnectionData(addr, 7777, "0.0.0.0"); // المضيف يستقبل من أي جهاز
        else utp.SetConnectionData(addr, 7777);
    }

    void ToggleJoinControls(bool on)
    {
        hostButton.gameObject.SetActive(on);
        clientButton.gameObject.SetActive(on);
        if (addressInput != null) addressInput.gameObject.SetActive(on);
    }

    void StartRound()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.StartRound();
    }

    void Update()
    {
        // أول ما تبدأ الجولة — اللوحة تختفي عند الجميع
        if (panel != null && panel.activeSelf &&
            GameManager.Instance != null &&
            GameManager.Instance.State.Value == GameManager.GameState.Playing)
        {
            panel.SetActive(false);
            Cursor.lockState = CursorLockMode.Locked;
        }
    }
}
