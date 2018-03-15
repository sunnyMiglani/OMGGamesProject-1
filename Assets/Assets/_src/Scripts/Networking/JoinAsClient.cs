using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class JoinAsClient : MonoBehaviour
{
    private GameSettings settings;
    public OverlayFader fader;

	private void Start()
	{
        if (Settings.gameSettings != null)
        {
            settings = Settings.gameSettings;
        }
        else
        {
            settings = ScriptableObject.CreateInstance<GameSettings>();
        }
	}
	//triggers when another object enters its area.
	private void OnTriggerEnter(Collider other)
    {
        //if object is player then load scene
        if (other.gameObject.tag == "Player")
        {
            if(fader == null) {
                StartCoroutine(Join());    
            } else {
                StartCoroutine(fader.FadeToBlackAndDo(Join()));
            }
        }
    }

    public IEnumerator Join() {
        if(NetworkManager.singleton == null) {
            yield return null;
        }
        string ipAddress = settings.IpAddress ?? "localhost";
        Debug.Log(settings);
        Debug.Log("Joining as client on " + ipAddress);
        NetworkManager.singleton.networkAddress = ipAddress;
        NetworkManager.singleton.StartClient(); //Join as client, only changes to online scene if server/host is on
        yield return new WaitForEndOfFrame();
    }
}
