using System.Collections.Generic;
using ChatdollKit.Model;

namespace ChatdollKit.Demo
{
    public class AGIARegistry
    {
        public enum AnimationCollection
        {
            AGIAFree,
            AGIA
        }

        public static Dictionary<AnimationCollection, Dictionary<string, Animation>> AnimationCollections { get; } = new()
        {
            {
                AnimationCollection.AGIAFree, new()
                {
                    {"angry_hands_on_waist", new Animation("BaseParam", 0, 3.0f)},
                    {"brave_hand_on_chest", new Animation("BaseParam", 1, 3.0f)},
                    {"calm_hands_on_back", new Animation("BaseParam", 2, 3.0f)},
                    {"concern_right_hand_front", new Animation("BaseParam", 3, 3.0f)},
                    {"energetic_right_fist_up", new Animation("BaseParam", 4, 3.0f)},
                    {"energetic_right_hand_piece", new Animation("BaseParam", 5, 3.0f)},
                    {"generic", new Animation("BaseParam", 6, 3.0f)},
                    {"pitiable_right_hand_on_back_head", new Animation("BaseParam", 7, 3.0f)},
                    {"surprise_hands_open_front", new Animation("BaseParam", 8, 3.0f)},
                    {"walking", new Animation("BaseParam", 9, 3.0f)},
                    {"waving_arm", new Animation("BaseParam", 10, 3.0f)},
                    {"look_away", new Animation("BaseParam", 6, 3.0f, "AGIA_Layer_look_away_01", "Additive Layer")},
                    {"nodding_once", new Animation("BaseParam", 6, 3.0f, "AGIA_Layer_nodding_once_01", "Additive Layer")},
                    {"swinging_body", new Animation("BaseParam", 6, 3.0f, "AGIA_Layer_swinging_body_01", "Additive Layer")},
                }
            },
            {
                AnimationCollection.AGIA, new()
                {
                    {"angry_hands_on_waist", new Animation("BaseParam", 0, 3.0f)},
                    {"angry_fists_front", new Animation("BaseParam", 1, 3.0f)},
                    {"boyish_right_hand_on_neck", new Animation("BaseParam", 2, 3.0f)},
                    {"brave_hand_on_chest", new Animation("BaseParam", 3, 3.0f)},
                    {"calm_hands_on_back", new Animation("BaseParam", 4, 3.0f)},
                    {"calm_hands_on_front", new Animation("BaseParam", 5, 3.0f)},
                    {"cat", new Animation("BaseParam", 6, 3.0f)},
                    {"classy_left_hand_on_waist", new Animation("BaseParam", 7, 3.0f)},
                    {"concern_right_hand_front", new Animation("BaseParam", 8, 3.0f)},
                    {"cry", new Animation("BaseParam", 9, 3.0f)},
                    {"cute_hands_on_front", new Animation("BaseParam", 10, 3.0f)},
                    {"cute_hands_stick_out", new Animation("BaseParam", 11, 3.0f)},
                    {"cute_leaning_forward", new Animation("BaseParam", 12, 3.0f)},
                    {"deny", new Animation("BaseParam", 13, 3.0f)},
                    {"energetic_right_fist_up", new Animation("BaseParam", 14, 3.0f)},
                    {"energetic_right_hand_piece", new Animation("BaseParam", 15, 3.0f)},
                    {"energetic_flex", new Animation("BaseParam", 16, 3.0f)},
                    {"fedup_slouching", new Animation("BaseParam", 17, 3.0f)},
                    {"fedup_right_hand_on_face", new Animation("BaseParam", 18, 3.0f)},
                    {"generic", new Animation("BaseParam", 19, 3.0f)},
                    {"laugh", new Animation("BaseParam", 20, 3.0f)},
                    {"pitiable_right_hand_on_back_head", new Animation("BaseParam", 21, 3.0f)},
                    {"point_finger", new Animation("BaseParam", 22, 3.0f)},
                    {"sexy_right_hand_pointy_finger", new Animation("BaseParam", 23, 3.0f)},
                    {"sexy_pose", new Animation("BaseParam", 24, 3.0f)},
                    {"sexy_lean_forward", new Animation("BaseParam", 25, 3.0f)},
                    {"stress_hands_on_back_head", new Animation("BaseParam", 26, 3.0f)},
                    {"surprise_hands_open_front", new Animation("BaseParam", 27, 3.0f)},
                    {"think", new Animation("BaseParam", 28, 3.0f)},
                    {"what", new Animation("BaseParam", 29, 3.0f)},
                    {"cat_emote", new Animation("BaseParam", 30, 3.0f)},
                    {"cute_emote", new Animation("BaseParam", 31, 3.0f)},
                    {"enegetic", new Animation("BaseParam", 32, 3.0f)},
                    {"point_finger_emote", new Animation("BaseParam", 33, 3.0f)},
                    {"running", new Animation("BaseParam", 34, 3.0f)},
                    {"walking", new Animation("BaseParam", 35, 3.0f)},
                    {"waving_arm", new Animation("BaseParam", 36, 3.0f)},
                    {"wave_hand", new Animation("BaseParam", 37, 3.0f)},
                    {"wave_hands", new Animation("BaseParam", 38, 3.0f)},
                    {"what_emote", new Animation("BaseParam", 39, 3.0f)},
                    {"laugh_down", new Animation("BaseParam", 19, 3.0f, "AGIA_Layer_laugh_down_01", "Additive Layer")},
                    {"laugh_up", new Animation("BaseParam", 19, 3.0f, "AGIA_Layer_laugh_up_01", "Additive Layer")},
                    {"look_away", new Animation("BaseParam", 19, 3.0f, "AGIA_Layer_look_away_01", "Additive Layer")},
                    {"look_away_angry", new Animation("BaseParam", 19, 3.0f, "AGIA_Layer_look_away_angry_01", "Additive Layer")},
                    {"nodding_once", new Animation("BaseParam", 19, 3.0f, "AGIA_Layer_nod_once_01", "Additive Layer")},
                    {"nodding_twice", new Animation("BaseParam", 19, 3.0f, "AGIA_Layer_nod_twice_01", "Additive Layer")},
                    {"shake_body", new Animation("BaseParam", 19, 3.0f, "AGIA_Layer_shake_body_01", "Additive Layer")},
                    {"shake_head", new Animation("BaseParam", 19, 3.0f, "AGIA_Layer_shake_head_01", "Additive Layer")},
                    {"surprise", new Animation("BaseParam", 19, 3.0f, "AGIA_Layer_surprise_01", "Additive Layer")},
                    {"swinging_body", new Animation("BaseParam", 19, 3.0f, "AGIA_Layer_swing_body_01", "Additive Layer")},
                    {"tilt_neck", new Animation("BaseParam", 19, 3.0f, "AGIA_Layer_tilt_neck_01", "Additive Layer")},
                    {"turn_left", new Animation("BaseParam", 19, 3.0f, "AGIA_Layer_turn_left_01", "Additive Layer")},
                    {"turn_right", new Animation("BaseParam", 19, 3.0f, "AGIA_Layer_turn_right_01", "Additive Layer")},
                }
            },
        };

        public static Dictionary<string, Animation> GetAnimations(AnimationCollection animationCollectionKey)
        {
            return AnimationCollections[animationCollectionKey];
        }
    }
}
