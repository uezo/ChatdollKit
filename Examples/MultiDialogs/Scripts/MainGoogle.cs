using UnityEngine;
using ChatdollKit.Examples.Dialogs;
using ChatdollKit.Extension.Google;

namespace ChatdollKit.Examples.MultiDialog
{
    [RequireComponent(typeof(WeatherSkill))]
    [RequireComponent(typeof(TranslateSkill))]
    [RequireComponent(typeof(EchoSkill))]
    [RequireComponent(typeof(Router))]
    public class MainGoogle : GoogleApplication
    {
        
    }
}
