using UnityEngine;
using UnityEngine.UI;
using ChatdollKit.SpeechListener;

namespace ChatdollKit.Demo
{
    public class SpeechListener : MonoBehaviour
    {
        public Text TextListened;
        private ISpeechListener speechListener;

        void Start()
        {
            speechListener = GetComponent<ISpeechListener>();
            speechListener.OnRecognized = async (text) =>
            {
                Debug.Log(text);
                TextListened.text = text;
            };
        }
    }
}
