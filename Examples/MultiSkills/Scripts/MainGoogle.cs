using UnityEngine;
using ChatdollKit.Examples.Skills;
using ChatdollKit.Extension.Google;

namespace ChatdollKit.Examples.MultiSkills
{
    [RequireComponent(typeof(WeatherSkill))]
    [RequireComponent(typeof(TranslateSkill))]
    [RequireComponent(typeof(EchoSkill))]
    [RequireComponent(typeof(Router))]
    public class MainGoogle : GoogleApplication
    {
        
    }
}
