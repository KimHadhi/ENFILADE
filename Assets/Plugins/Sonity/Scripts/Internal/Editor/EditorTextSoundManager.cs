// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

#if UNITY_EDITOR

using UnityEngine;

namespace Sonity.Internal {

    public class EditorTextSoundManager {

        public static readonly string soundManagerTooltip =
        $"The {NameOf.SoundManager} is the master object which is used to play sounds and manage global settings." + "\n" +
        "\n" +
        $"An instance of this object is required in the scene in order to play {NameOf.SoundEvent}s." + "\n" +
        "\n" +
        $"You can add the pre-made prefab called “SoundManager” found in “Assets\\Plugins\\Sonity\\Prefabs” to your scene." + "\n" +
        "\n" +
        $"Or you can add the “Sonity - Sound Manager” component to an empty {nameof(GameObject)} in the scene, it works just as well." + "\n" +
        "\n" +
        $"Tip: If you need to play {NameOf.SoundEvent}s on Awake() when starting your game you need to edit the Script Execution Order." + "\n" +
        "\n" +
        $"Go to \"Project Settings...\" -> \"Script Execution Order\" and add \"Sonity.SoundManager\"." + "\n" +
        "\n" +
        $"Then set it to a negative value (like -50) so it loads before the code which you want to use Awake() to play sounds when starting your game." + EditorTrial.trialTooltip;

        // Warnings (no trial tooltips)
        public static readonly string speedOfSoundScaleWarning = "Speed of Sound Scale is 0. It will have no effect";
        public static readonly string disablePlayingSoundsWarning = $"No {NameOf.SoundEvent}s can be played";
        public static readonly string audioSettingsRealVoicesWarning = $"Real Voices are lower than Voice Limit";
        public static readonly string audioSettingsVirtualVoicesWarning = $"Virtual Voices are lower than Real Voices";

        // Legacy Warnings (no trial tooltips)
        public static readonly string missingSonityGuidsWarning = $"Sonity Assets have missing {NameOf.SonityGuid}s";
        public static readonly string missingSonityGuidsReserializeAllAssetsButtonLabel = 
            $"\n" +
            $"Sonity 1.2.0 Upgrade\n" +
            $"Missing {NameOf.SonityGuid}s\n" +
            $"Press Me → Reserialize - All Sonity Assets ← Press Me\n" +
            $"\n" +
            $"(save first, will change a lot of assets)\n" +
            $"\n" +
            $"If you have any issues restarting Unity is recommended." +
            $"\n";

        public static readonly string missingSonityGuidsReserializeAllAssetsButtonTooltip =
        $"In Sonity 1.2.0 asset IDs are reworked into new SonityGuids which are needed to play sounds." + "\n" +
        "\n" +
        $"When you press this button it will run the action:" + "\n" +
        "\n" +
        $"\"Tools/Sonity 🔊/Reserialize - All Sonity Assets\"" + "\n" +
        "\n" +
        $"Which will reserialize all your Sonity Assets (save all your other work before running this)." + EditorTrial.trialTooltip;

        // Reset Settings
        public static readonly string resetSettingsLabel = "Reset Settings";
        public static readonly string resetSettingsTooltip = "Resets all settings." + EditorTrial.trialTooltip;

        // Reset All
        public static readonly string resetAllLabel = "Reset All";
        public static readonly string resetAllTooltip = "Resets settings and statistics." + EditorTrial.trialTooltip;

        // Free Trial Text
        public static readonly string enableSoundInBuildsLabel = "Enable Sound in Builds";
        public static readonly string enableSoundInBuildsTooltip =
            "This feature is removed from the Free Trial version of Sonity." + "\n" +
            "\n" + " Please buy the full version to get this feature." + EditorTrial.trialTooltip;
        public static readonly string enableSoundInBuildsWarning = $"Sounds in Builds is not Available in the Free Trial Version of Sonity";

        // Settings ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        public static readonly string disablePlayingSoundsLabel = "Disable Playing Sounds";
        public static readonly string disablePlayingSoundsTooltip =
            "Disables all the Play functionality." + "\n" +
            "\n" +
            "Useful if you've for example implemented temp sounds and don't want everyone else to hear them." + EditorTrial.trialTooltip;

        public static readonly string soundTimeScaleLabel = $"Sound Time Scale";
        public static readonly string soundTimeScaleTooltip =
            $"Change the Time Scale used for sound related calculations ingame." + "\n" +
            "\n" +
            $"When the game is not playing in the editor, only Time.realtimeSinceStartup can be used (for preview, etc)." + "\n" +
            "\n" +
            $"The Time Scale is used to calculate:" + "\n" +
            $"- Modifier Delay" + "\n" +
            $"- Modifier Fade In/Out Length" + "\n" +
            $"- SoundEvent Timeline Delay" + "\n" +
            $"- SoundEvent Cooldown Time" + "\n" +
            $"- SoundEvent Intensity Seek Time" + "\n" +
            $"- SoundContainer Prevent End Clicks Fade" + "\n" +
            $"- SoundParameter Delay" + "\n" +
            $"- SoundManager Speed of Sound Delay" + "\n" +
            $"- SoundManager Voice Disable Time" + "\n" +
            $"- SoundManager SoundEvent Debug Live Fade" + EditorTrial.trialTooltip;

        public static readonly string globalPauseLabel = $"Global Pause";
        public static readonly string globalPauseTooltip =
            $"Pauses or unpauses all sound using {nameof(AudioListener)}.pause." + "\n" +
            "\n" +
            $"Except the {NameOf.SoundEvent}s which are set to \"Ignore Global Pause\"." + "\n" +
            "\n" +
            $"Note that because {nameof(AudioListener)}.pause is used, all other non-Sonity AudioSources will also be paused." + "\n" +
            "\n" +
            $"This can be remedied with using AudioSource.ignoreListenerPause." + "\n" +
            "\n" +
            $"Can be set with {nameof(SoundManagerBase.Internals.SetGlobalPause)}() and {nameof(SoundManagerBase.Internals.SetGlobalUnpause)}() in the SoundManager." + EditorTrial.trialTooltip;

        public static readonly string globalVolumeLabel = $"Global Volume dB";
        public static readonly string globalVolumeTooltip =
            $"Sets the global volume using {nameof(AudioListener)}.volume." + "\n" +
            "\n" +
            $"Note that because {nameof(AudioListener)}.volume is used, all other non-Sonity AudioSources will also be affected." + "\n" +
            "\n" +
            $"This can be remedied with using AudioSource.ignoreListenerVolume." + "\n" +
            "\n" +
            $"Can be set with {nameof(SoundManagerBase.Internals.SetGlobalVolumeDecibel)}() and {nameof(SoundManagerBase.Internals.SetGlobalVolumeRatio)}() in the SoundManager." + EditorTrial.trialTooltip;

        public static readonly string audioListenerVolumeIncreaseEnableLabel = $"Enable Volume Increase";
        public static readonly string audioListenerVolumeIncreaseEnableTooltip =
            $"If enabled you get the ability to raise the volume of {NameOf.SoundContainer}s and {NameOf.SoundEvent}s by +24dB each and {NameOf.SoundVolumeGroup}s by +12dB." + "\n" +
            "\n" +
            $"This is done by increasing the {nameof(AudioListener)}.volume +60dB and reducing the volume of the AudioSource to compensate." + "\n" +
            "\n" +
            $"Note that all other non-Sonity AudioSources will also be affected, you can avoid this by enabling AudioSource.ignoreListenerVolume." + "\n" +
            "\n" +
            $"For volume increase to work, you need to set the Scripting Define Symbol \"SONITY_ENABLE_VOLUME_INCREASE\"." + "\n" +
            "\n" +
            $"This can be done by pressing the button below (make sure all platforms has this scripting define symbol)." + "\n" +
            "\n" +
            $"You also need to have a {NameOf.SoundManager} in your scene for volume increase to work when previewing and ingame." + "\n" +
            "\n" +
            $"The free trial version of Sonity doesn’t support this feature because it uses Scripting Define Symbols." + EditorTrial.trialTooltip;

        public static readonly string audioListenerVolumeIncreaseFreeTrialWarningLabel = $"Volume Increase is not Available in the Free Trial Version of Sonity";

        public static readonly string audioListenerVolumeIncreaseEducationalVersionWarningLabel = $"Volume Increase is not Available in the Educational Version of Sonity";

        public static readonly string audioListenerVolumeIncreaseScriptingDefineSymbolAddLabel = $"Add Scripting Define Symbol:\n\"SONITY_ENABLE_VOLUME_INCREASE\"";
        public static readonly string audioListenerVolumeIncreaseScriptingDefineSymbolAddTooltip =
            $"Pressing on this button will automatically add the Scripting Define Symbol \"SONITY_ENABLE_VOLUME_INCREASE\" if it doesn't exist." + "\n" +
            "\n" +
            $"This enables you to raise the volume of {NameOf.SoundEvent}s and {NameOf.SoundEvent}s." + EditorTrial.trialTooltip;

        public static readonly string audioListenerVolumeIncreaseScriptingDefineSymbolRemoveLabel = $"Remove Scripting Define Symbol:\n\"SONITY_ENABLE_VOLUME_INCREASE\"";
        public static readonly string audioListenerVolumeIncreaseScriptingDefineSymbolRemoveTooltip =
            $"Pressing on this button will automatically remove the Scripting Define Symbol \"SONITY_ENABLE_VOLUME_INCREASE\" if it exists." + "\n" +
            "\n" +
            $"This removes the ability to raise the volume of {NameOf.SoundEvent}s and {NameOf.SoundEvent}s." + EditorTrial.trialTooltip;

        public static readonly string globalSoundTagsLabel = $"Global {NameOf.SoundTag}s";
        public static readonly string globalSoundTagsTooltip =
            $"The current Global SoundTags." + "\n" +
            "\n" +
            $"They will affect what SoundEvents using global SoundTags are playing." + "\n" +
            "\n" +
            $"This way you can change one Global SoundTag and it will change all the SoundEvents in the project." + "\n" +
            "\n" +
            $"Useful for controlling e.g:" + "\n" +
            "\n" +
            $"Gun reflection/reverb layers - based on the area the player is in (indoor/outdoor etc)." + "\n" +
            "\n" +
            $"Misophony mode - replacing eating/drinkings sound with less offensive sounds." + EditorTrial.trialTooltip;

        public static readonly string integrationsLabel = "Integrations";
        public static readonly string integrationsTooltip =
            "Settings for different Sonity integrations." + "\n" +
            "\n" +
            "These usually require an additional package to be added your Unity project." + EditorTrial.trialTooltip;

        public static readonly string steamAudioLabel = "Steam Audio";
        public static readonly string steamAudioTooltip =
            "Enables the integration between Sonity and the third party asset Steam Audio." + "\n" +
            "First you need to install the Steam Audio Unity asset package." + "\n" +
            "You also need to add the Script Define Symbol \"SONITY_ENABLE_INTEGRATION_STEAM_AUDIO\"." + "\n" +
            "The free trial version of Sonity doesn’t support this feature because it uses Scripting Define Symbols." + "\n" +
            "It is tested and working on Steam Audio version 4.6.1." + "\n" +
            "\n" +
            "Steam Audio adds features like:" + "\n" +
            "Occlusion of sound sources." + "\n" +
            "Reflections and related environmental audio effects." + "\n" +
            "Propagation of sound along multiple paths." + "\n" +
            "\n" +
            "When using Steam Audio in combination with Voice Preload you should enable Async Startup because Steam Audio isn't ready on Awake()." + "\n" +
            "Also note that Steam Audio at the moment is not available for all platforms." + "\n" +
            "\n" +
            "Steam Audio - Getting Started" + "\n" +
            "1. Go to the valve github https://github.com/ValveSoftware/steam-audio/releases" + "\n" +
            "2. Download steamaudio_unity_X.X.X.zip and locate the file SteamAudio.unitypackage." + "\n" +
            "3. Drag and drop the Steam Audio unitypackage into Unity and import the contents." + "\n" +
            "4. Enable “Steam Audio” in the SoundManager Settings." + "\n" +
            "5. Add the Script Define Symbol \"SONITY_ENABLE_INTEGRATION_STEAM_AUDIO\" by pressing the “Add Scripting Define Symbol” button." + "\n" +
            "6. You may need to restart the Unity project." + "\n" +
            $"This adds the SoundContainer Settings - Steam Audio tab where you can select how the sound will be played in Steam Audio." + EditorTrial.trialTooltip;

        public static readonly string steamAudioScriptingDefineSymbolAddLabel = $"Add Scripting Define Symbol:\n\"SONITY_ENABLE_INTEGRATION_\nSTEAM_AUDIO\"";
        public static readonly string steamAudioScriptingDefineSymbolAddTooltip =
            $"Pressing on this button will automatically add the Scripting Define Symbol \"SONITY_ENABLE_INTEGRATION_STEAM_AUDIO\" if it doesn't exist." + "\n" +
            "\n" +
            $"This enables the integration between Sonity and Steam Audio for Unity." + "\n" +
            "\n" +
            $"You are required to install the Steam Audio asset package separately." + EditorTrial.trialTooltip;

        public static readonly string steamAudioScriptingDefineSymbolRemoveLabel = $"Remove Scripting Define Symbol:\n\"SONITY_ENABLE_INTEGRATION_\nSTEAM_AUDIO\"";
        public static readonly string steamAudioScriptingDefineSymbolRemoveTooltip =
            $"Pressing on this button will automatically remove the Scripting Define Symbol \"SONITY_ENABLE_INTEGRATION_STEAM_AUDIO\" if it exists." + "\n" +
             "\n" +
            $"This removes the integration between Sonity and Steam Audio for Unity." + EditorTrial.trialTooltip;

        public static readonly string steamAudioPreloadVoicesAsyncLoadWarning = "Steam Audio in combination with Voice Preload requires Async Startup";

        public static readonly string steamAudioSpatializeDefaultOnLabel = "Spatialize Default On";
        public static readonly string steamAudioSpatializeDefaultOnTooltip =
            $"Makes HRTF Plugin Spatialize in the {NameOf.SoundContainer} Settings be enabled by default when creating new assets." + "\n" +
            "\n" +
            $"Spatialize makes the sound be affected by Steam Audio with effects like occlusion, reflections and more." + "\n" +
            "\n" +
            $"Disable Spatialize for 2D sounds like music etc." + EditorTrial.trialTooltip;

        public static readonly string playMakerLabel = "PlayMaker";
        public static readonly string playMakerTooltip =
            "Enables the integration between Sonity and the third party asset PlayMaker by adding custom Sonity actions." + "\n" +
            "\n" +
            "First you need to install the PlayMaker Unity asset package." + "\n" +
            "\n" +
            "You also need to add the Script Define Symbol \"SONITY_ENABLE_INTEGRATION_PLAYMAKER\"." + "\n" +
            "\n" +
            "The free trial version of Sonity doesn’t support this feature because it uses Scripting Define Symbols." + "\n" +
            "\n" +
            "It is tested and working on PlayMaker version 1.9.8." + "\n" +
            "\n" +
            "PlayMaker adds easy access to visual scripting." + EditorTrial.trialTooltip;

        public static readonly string playMakerScriptingDefineSymbolAddLabel = $"Add Scripting Define Symbol:\n\"SONITY_ENABLE_INTEGRATION_\nPLAYMAKER\"";
        public static readonly string playMakerScriptingDefineSymbolAddTooltip =
            $"Pressing on this button will automatically add the Scripting Define Symbol \"SONITY_ENABLE_INTEGRATION_PLAYMAKER\" if it doesn't exist." + "\n" +
            "\n" +
            $"This enables the integration between Sonity and PlayMaker." + "\n" +
            "\n" +
            $"You are required to install the PlayMaker asset package separately." + EditorTrial.trialTooltip;

        public static readonly string playMakerScriptingDefineSymbolRemoveLabel = $"Remove Scripting Define Symbol:\n\"SONITY_ENABLE_INTEGRATION_\nPLAYMAKER\"";
        public static readonly string playMakerScriptingDefineSymbolRemoveTooltip =
            $"Pressing on this button will automatically remove the Scripting Define Symbol \"SONITY_ENABLE_INTEGRATION_PLAYMAKER\" if it exists." + "\n" +
            "\n" +
            $"This removes the integration between Sonity and PlayMaker." + EditorTrial.trialTooltip;

        // SoundEvent Library
        public static readonly string soundEventLibraryScriptingDefineSymbolAddLabel = $"Add Scripting Define Symbol:\n\"SONITY_ENABLE_\nSOUNDEVENT_LIBRARY\"";
        public static readonly string soundEventLibraryScriptingDefineSymbolAddTooltip =
                    $"Pressing on this button will automatically add the Scripting Define Symbol \"SONITY_ENABLE_SOUNDEVENT_LIBRARY\" if it doesn't exist." + "\n" +
                    "\n" +
                    $"" + "\n" +
                    "\n" +
                    $"" + EditorTrial.trialTooltip;

        public static readonly string soundEventLibraryScriptingDefineSymbolRemoveLabel = $"Remove Scripting Define Symbol:\n\"SONITY_ENABLE_\nSOUNDEVENT_LIBRARY\"";
        public static readonly string soundEventLibraryScriptingDefineSymbolRemoveTooltip =
            $"Pressing on this button will automatically remove the Scripting Define Symbol \"SONITY_ENABLE_SOUNDEVENT_LIBRARY\" if it exists." + "\n" +
            "\n" +
            $"" + EditorTrial.trialTooltip;

        // EntitySound Base
        public static readonly string entitySoundBaseScriptingDefineSymbolAddLabel = $"Add Scripting Define Symbol:\n\"SONITY_ENABLE_\nENTITY_SOUND_BASE\"";
        public static readonly string entitySoundBaseScriptingDefineSymbolAddTooltip =
                    $"Pressing on this button will automatically add the Scripting Define Symbol \"SONITY_ENABLE_ENTITY_SOUND_BASE\" if it doesn't exist." + "\n" +
                    "\n" +
                    $"This enables the integration between Sonity and Unity's ECS framework." + "\n" +
                    "\n" +
                    $"You are required to install the Entities package separately." + EditorTrial.trialTooltip;

        public static readonly string entitySoundBaseScriptingDefineSymbolRemoveLabel = $"Remove Scripting Define Symbol:\n\"SONITY_ENABLE_\nENTITY_SOUND_BASE\"";
        public static readonly string entitySoundBaseScriptingDefineSymbolRemoveTooltip =
            $"Pressing on this button will automatically remove the Scripting Define Symbol \"SONITY_ENABLE_ENTITY_SOUND_BASE\" if it exists." + "\n" +
            "\n" +
            $"This removes the integration between Sonity and Unity's ECS framework." + EditorTrial.trialTooltip;

        // EntitySound Library
        public static readonly string entitySoundLibraryScriptingDefineSymbolAddLabel = $"Add Scripting Define Symbol:\n\"SONITY_ENABLE_\nENTITY_SOUND_LIBRARY\"\n+ EntitySound Base";
        public static readonly string entitySoundLibraryScriptingDefineSymbolAddTooltip =
                    $"Pressing on this button will automatically add the Scripting Define Symbol \"SONITY_ENABLE_ENTITY_SOUND_LIBRARY\" if it doesn't exist." + "\n" +
                    "\n" +
                    $"" + "\n" +
                    "\n" +
                    $"" + EditorTrial.trialTooltip;

        public static readonly string entitySoundLibraryScriptingDefineSymbolRemoveLabel = $"Remove Scripting Define Symbol:\n\"SONITY_ENABLE_\nENTITY_SOUND_LIBRARY\"";
        public static readonly string entitySoundLibraryScriptingDefineSymbolRemoveTooltip =
            $"Pressing on this button will automatically remove the Scripting Define Symbol \"SONITY_ENABLE_ENTITY_SOUND_LIBRARY\" if it exists." + "\n" +
            "\n" +
            $"" + EditorTrial.trialTooltip;

        public static readonly string distanceScaleLabel = "Distance Scale";
        public static readonly string distanceScaleTooltip =
            "Global range scale multiplier for all the sounds in Sonity." + "\n" +
            "\n" +
            "Distance is calculated by Unity units of distance." + "\n" +
            "\n" +
            $"E.g. if Distance Scale is set to 100, a {NameOf.SoundEvent} with the distance multiplier of 1 will be heard up to 100 Unity units away." + EditorTrial.trialTooltip;

        public static readonly string overrideListenerDistanceLabel = "Override Listener Distance";
        public static readonly string overrideListenerDistanceTooltip =
            $"If enabled an {NameOf.AudioListenerDistance} component is required in the scene." + "\n" +
            "\n" +
            $"The position of the {NameOf.AudioListenerDistance} component will determine all distance based calculations (like volume falloff)." + "\n" +
            "\n" +
            $"While the AudioListener position will be used for spatialization and Angle to Stereo Pan calculations." + "\n" +
            "\n" +
            $"Example of usage in a 3rd person or top down game:" + "\n" +
            "\n" +
            $"Enable \"Override Listener Distance\" in the {NameOf.SoundManager}." + "\n" +
            "\n" +
            $"Put the AudioListener on the main camera and the {NameOf.AudioListenerDistance} on the player character." + "\n" +
            "\n" +
            $"Try changing the Amount slider to find a nice balance between the different positions." + EditorTrial.trialTooltip;

        public static readonly string overrideListenerDistanceAmountLabel = "Amount %";
        public static readonly string overrideListenerDistanceAmountTooltip =
            $"How much weight the {NameOf.AudioListenerDistance} position has over the AudioListener position." + "\n" +
            "\n" +
            $"The position is linearly interpolated between the two of them." + "\n" +
            "\n" +
            $"100% is at the {NameOf.AudioListenerDistance} component position." + "\n" +
            "\n" +
            $"50% is halfway between them." + "\n" +
            "\n" +
            $"0% is at the AudioListener position.";

        public static readonly string speedOfSoundEnabledLabel = "Enable Speed of Sound";
        public static readonly string speedOfSoundEnabledTooltip =
            $"Speed of sound is a delay based on the distance between the Audio Listener and a {NameOf.SoundEvent}." + EditorTrial.trialTooltip;

        public static readonly string speedOfSoundScaleLabel = "Speed of Sound Scale";
        public static readonly string speedOfSoundScaleTooltip =
            $"Global speed of sound delay scale multiplier." + "\n" +
            "\n" +
            $"1 equals 430 Unity units per second." + "\n" +
            "\n" +
            $"Is calculated using the time scale selected in the {NameOf.SoundManager}." + EditorTrial.trialTooltip;

        public static readonly string addressableAudioMixerUseLabel = $"Addressable AudioMixer";
        public static readonly string addressableAudioMixerUseTooltip =
            $"If enabled, all AudioMixer references in all {NameOf.SoundEvent}s and {NameOf.SoundContainer}s to the selected addressable AudioMixer Reference Asset." + "\n" +
            "\n" +
            $"This is to fix problems when the AudioMixer is an addressable asset because it might create both a normal and an addressable instance with different IDs." + "\n" +
            "\n" +
            $"Note that when using an addressable AudioMixer you should also enable Async Startup to increase startup speed." + "\n" +
            "\n" +
            $"The AudioMixerGroups are matched using FindMatchingGroups using the name of the AudioMixerGroup, so you have to name your groups to something unique." + "\n" +
            "\n" +
            $"For builds the AudioMixerGroups are cached and wont be updated once the sound has been played." + "\n" +
            "\n" +
            $"When using AudioMixer.SetFloat() etc you need to access this specific AudioMixer instance by using \"SoundManager.Instance.GetAddressableAudioMixer();\"." + "\n" +
            "\n" +
            $"For addressable AudioMixer to work, you need to add the package \"com.unity.addressables\"." + "\n" +
            "\n" +
            $"You also need to define the Script Define Symbol \"SONITY_ENABLE_ADDRESSABLE_AUDIOMIXER\"." + "\n" +
            "\n" +
            $"Asmdef_Sonity.Internal.Runtime references Unity.Addressables and Unity.ResourceManager." + "\n" +
            "\n" +
            $"Asmdef_Sonity.Internal.Editor references Unity.Addressables." + "\n" +
            "\n" +
            $"The free trial version of Sonity doesn’t support this feature because it uses Scripting Define Symbols." + EditorTrial.trialTooltip;

        public static readonly string addressableAudioMixerAsyncLoadWarning = $"When using addressable AudioMixer you should enable Async Startup";

        public static readonly string addressableAudioMixerReferenceLabel = $"AudioMixer Reference";
        public static readonly string addressableAudioMixerReferenceTooltip =
            $"Assign the AudioMixer reference here, it will be automatically loaded." + "\n" +
            "\n" +
            $"Only one AudioMixer per project is supported with this feature." + EditorTrial.trialTooltip;

        public static readonly string addressableAudioMixerScriptingDefineSymbolAddLabel = $"Add Scripting Define Symbol:\n\"SONITY_ENABLE_ADDRESSABLE_AUDIOMIXER\"";
        public static readonly string addressableAudioMixerScriptingDefineSymbolAddTooltip =
            $"Pressing on this button will automatically add the Scripting Define Symbol \"SONITY_ENABLE_ADDRESSABLE_AUDIOMIXER\" if it doesn't exist." + "\n" +
            "\n" +
            $"This is so the \"com.unity.addressables\" asset package isn't needed if you don't use the addressable AudioMixer." + EditorTrial.trialTooltip;

        public static readonly string addressableAudioMixerScriptingDefineSymbolRemoveLabel = $"Remove Scripting Define Symbol:\n\"SONITY_ENABLE_ADDRESSABLE_AUDIOMIXER\"";
        public static readonly string addressableAudioMixerScriptingDefineSymbolRemoveTooltip =
            $"Pressing on this button will automatically remove the Scripting Define Symbol \"SONITY_ENABLE_ADDRESSABLE_AUDIOMIXER\" if it exists." + "\n" +
            "\n" +
            $"This is so the \"com.unity.addressables\" asset package isn't needed if you don't use the addressable AudioMixer." + EditorTrial.trialTooltip;

        public static readonly string addressableAudioMixerFreeTrialWarningLabel = $"Addressable AudioMixer is not Available in the Free Trial Version of Sonity";
        public static readonly string addressableAudioMixerEducationalVersionWarningLabel = $"Addressable AudioMixer is not Available in the Educational Version of Sonity";

        public static readonly string addressableAudioMixerAsyncLoadInEditorLabel = $"Async Load In Editor";
        public static readonly string addressableAudioMixerAsyncLoadInEditorTooltip =
            $"Enables async loading of the addressable AudioMixer in the editor." + "\n" +
            "\n" +
            $"Having it disabled is useful for testing in the editor when you need to access the AudioMixer or play sounds on Awake before async loading is finished." + EditorTrial.trialTooltip;

        public static readonly string asyncStartupLabel = $"Async Startup";
        public static readonly string asyncStartupTooltip =
            $"Makes Sonity initialize using an async loading method." + "\n" +
            "\n" +
            $"This is useful for when you use the addressable AudioMixer to speed up load times." + "\n" +
            "\n" +
            $"Or when using Steam Audio and you want to preload voices." + EditorTrial.trialTooltip;

        public static readonly string debugWarningsLabel = "Debug Warnings";
        public static readonly string debugWarningsTooltip =
            "Makes Sonity output Debug Warnings if anything is wrong." + EditorTrial.trialTooltip;

        public static readonly string debugInPlayModeLabel = "Debug In Play Mode";
        public static readonly string debugInPlayModeTooltip =
            "Makes Sonity output Debug Warnings if anything is wrong in Play Mode." + EditorTrial.trialTooltip;

        public static readonly string guiWarningsLabel = "GUI Warnings";
        public static readonly string guiWarningsTooltip =
            "Makes Sonity show GUI Warnings if anything is wrong in the editor." + EditorTrial.trialTooltip;

        public static readonly string dontDestoyOnLoadLabel = "Use DontDestroyOnLoad()";
        public static readonly string dontDestoyOnLoadTooltip =
            "Calls DontDestroyOnLoad() at Start for Sonity objects." + "\n" +
            "\n" +
            "Which makes them persistent when switching scenes." + "\n" +
            "\n" +
            "For this to work the parent is set to null, which can move the objects." + EditorTrial.trialTooltip;

        // Performance
        public static readonly string playOneShotOptimizeLabel = $"PlayOneShot Optimize";
        public static readonly string playOneShotOptimizeTooltip =
            $"If enabled then then Sonity will try to play Voices with PlayOneShot() instead of Play()." + "\n" +
            "\n" +
            $"Some features are disabled when using PlayOneShot, like:" +
            $"SoundContainer PreventEndClicks" + "\n" +
            $"SoundEvent.GetLastPlayedClipTimeSeconds()" + "\n" +
            $"SoundEvent.GetLastPlayedClipTimeRatio()" + "\n" +
            "\n" +
            $"But it will increase performance, so it might be useful for low-end platforms." + "\n" +
            "\n" +
            $"If disabled in the SoundContainer, PlayOneShot will not be used." + "\n" +
            $"Which is useful if you need get etc AudioSource.time from oneshots etc." + "\n" +
            "\n" +
            $"It is used under certain conditions and is evaluated in this order:" + "\n" +
            $"Skipped if: Loop is enabled" + "\n" +
            $"Skipped if: Trigger On Tail is enabled" + "\n" +
            $"Skipped if: Random Start Position is enabled" + "\n" +
            $"Skipped if: Start Postion is more than 0 (which it also is when playing reversed)" + "\n" +
            $"Enabled if: Distance is disabled and Spatial Blend is 0" + "\n" +
            $"Enabled if: Force 2D is enabled" + "\n" +
            $"Skipped if: Follow Position is enabled, in the SoundContainer or Parameters" + "\n" +
            $"Enabled if: None of the above is triggered" + EditorTrial.trialTooltip;

        public static readonly string soundManagerManualUpdateLabel = $"Manual Update";
        public static readonly string soundManagerManualUpdateTooltip =
            $"If enabled then the SoundManagers MonoBehaviour Update() will be replaced with ManualUpdate()." + "\n" +
            "\n" +
            $"This way Sonity won't automatically update every frame." + "\n" +
            "\n" +
            $"You need to call SoundManager.Instance.ManualUpdate() manually." + "\n" +
            "\n" +
            $"Giving you full control how often and when in the frame the SoundManager should update." + "\n" +
            "\n" +
            $"Requires the script define symbol \"SONITY_ENABLE_SOUNDMANAGER_MANUAL_UPDATE\"." + EditorTrial.trialTooltip;

        public static readonly string soundManagerManualUpdateScriptingDefineSymbolAddLabel = $"Add Scripting Define Symbol:\n\"SONITY_ENABLE_\nSOUNDMANAGER_MANUAL_UPDATE\"";
        public static readonly string soundManagerManualUpdateScriptingDefineSymbolAddTooltip =
                    $"Pressing on this button will automatically add the Scripting Define Symbol \"SONITY_ENABLE_SOUNDMANAGER_MANUAL_UPDATE\" if it doesn't exist." + "\n" +
                    "\n" +
                    $"This disables automatic update and enables manual update of the SoundManager with SoundManager.Instance.ManualUpdate()." + EditorTrial.trialTooltip;

        public static readonly string soundManagerManualUpdateScriptingDefineSymbolRemoveLabel = $"Remove Scripting Define Symbol:\n\"SONITY_ENABLE_\nSOUNDMANAGER_MANUAL_UPDATE\"";
        public static readonly string soundManagerManualUpdateScriptingDefineSymbolRemoveTooltip =
            $"Pressing on this button will automatically remove the Scripting Define Symbol \"SONITY_ENABLE_SOUNDMANAGER_MANUAL_UPDATE\" if it exists." + "\n" +
            "\n" +
            $"This enables automatic update and removes manual update of the SoundManager with SoundManager.Instance.ManualUpdate()." + EditorTrial.trialTooltip;

        public static readonly string voiceLimitLabel = "Voice Limit";
        public static readonly string voiceLimitTooltip =
            "Maximum number of Voices." + "\n" +
            "\n" +
            "If the limit is reached it will steal the Voice with the lowest priority." + "\n" +
            "\n" +
            "If you need extra performance, you could try lowering the real and virtual voices to a lower number." + "\n" +
            "\n" +
            "Voice Limit cannot be lower than Voice Preload." + EditorTrial.trialTooltip;

        public static readonly string voicePreloadLabel = "Voice Preload";
        public static readonly string voicePreloadTooltip =
            "How many Voices to preload on Awake()." + "\n" +
            "\n" +
            "Voice Limit cannot be lower than Voice Preload." + EditorTrial.trialTooltip;

        public static readonly string audioSettingsRealVoicesLabel = "Max Real Voices";
        public static readonly string audioSettingsVirtualVoicesLabel = "Max Virtual Voices";
        public static readonly string audioSettingsRealAndVirtualVoicesTooltip =
            $"Max Real Voices:" + "\n" +
            $"The maximum number of real (heard) {nameof(AudioSource)}s that can be played at the same time." + "\n" +
            "\n" +
            $"\"Real Voices\" should be the same as the \"Voice Limit\", or more if you play other sounds outside of Sonity." + "\n" +
            "\n" +
            $"Max Virtual Voices:" + "\n" +
            $"The maximum number of virtual (not heard) {nameof(AudioSource)}s that can be played at the same time." + "\n" +
            "\n" +
            $"This should always be more than the number of real voices." + "\n" +
            "\n" +
            "You can change these values manually in:" + "\n" +
            "\"Edit\" > \"Project Settings\" > \"Audio\"" + EditorTrial.trialTooltip;

        public static readonly string applyVoiceLimitToAudioSettingsLabel = "Apply to Project Audio Settings";
        public static readonly string applyVoiceLimitToAudioSettingsTooltip =
            "Applies the Voice Limit to the Project Audio Settings." + "\n" +
            "\n" +
            "Sets \"Real Voices\" to the \"Voice Limit\"." + "\n" +
            "\n" +
            "You can change these values manually in:" + "\n" +
            "\"Edit\" > \"Project Settings\" > \"Audio\"" + EditorTrial.trialTooltip;

        public static readonly string voiceStopTimeLabel = "Voice Disable Time";
        public static readonly string voiceStopTimeTooltip =
            $"How long in seconds to wait before disabling a Voice when they've stopped playing. " + "\n" +
            "\n" +
            $"Retriggering a voice which is not disabled is more performant than retriggering a voice which is disabled." + "\n" +
            "\n" +
            $"But having a lot of voices enabled which aren't used is also not good for performance, so don't set this value too high." + "\n" +
            "\n" +
            $"Is calculated using the time scale selected in the {NameOf.SoundManager}." + EditorTrial.trialTooltip;

        public static readonly string voiceEffectLimitLabel = "Voice Effect Limit";
        public static readonly string voiceEffectLimitTooltip =
            "Maximum number of Voice Effects which can be used at the same time." + "\n" +
            "\n" +
            "A Voice with any combination of waveshaper/lowpass/highpass counts as one Voice Effect." + "\n" +
            "\n" +
            "If the values of a Voice Effect doesn't have any effect it is disabled automatically (e.g. distortion amount is 0)." + "\n" +
            "\n" +
            "If the Voice Effect limit is reached, the Voice Effects are prioritized by the Voices with the highest volume * priority." + "\n" +
            "\n" +
            "Watch out for high load on the audio thread if set too high." + "\n" +
            "\n" +
            "Try setting the buffer size to \"Best Performance\" in \"Edit\" > \"Project Settings\" > \"Audio\" if you want to run more Voice Effects." + EditorTrial.trialTooltip;

        public static readonly string voiceEffectDisableLabel = "Disable Voice Effects";
        public static readonly string voiceEffectDisableTooltip =
            "If disabled then any Voice Effects (lowpass/highpass/distortion) will not have any affect." + "\n" +
            "\n" +
            "It removes the Voice Effect components this makes Sonity not use OnAudioFilterRead() at all." + "\n" +
            "\n" +
            "This is useful for saving performance on low end systems (like the Nintendo Switch 1)." + "\n" +
            "\n" +
            "To enable this behaviour you need to add the script define symbol:" + "\n" +
            "\n" +
            "\"SONITY_DISABLE_VOICE_EFFECTS\"" + "\n" +
            "\n" +
            "You can also override this per platform with the script define symbol." + "\n" +
            "\n" +
            "This also applies to the editor sound preview." + EditorTrial.trialTooltip;

        public static readonly string voiceEffectDisableScriptDefineAddLabel = $"Add Scripting Define Symbol:\n\"SONITY_DISABLE_VOICE_EFFECTS\"";
        public static readonly string voiceEffectDisableScriptDefineAddTooltip =
            $"Pressing on this button will automatically add the Scripting Define Symbol \"SONITY_DISABLE_VOICE_EFFECTS\" if it doesn't exist." + "\n" +
            "\n" +
            $"" + EditorTrial.trialTooltip;

        public static readonly string voiceEffectDisableScriptDefineRemoveLabel = $"Remove Scripting Define Symbol:\n\"SONITY_DISABLE_VOICE_EFFECTS\"";
        public static readonly string voiceEffectDisableScriptDefineRemoveTooltip =
            $"Pressing on this button will automatically remove the Scripting Define Symbol \"SONITY_DISABLE_VOICE_EFFECTS\" if it exists." + "\n" +
            "\n" +
            $"" + EditorTrial.trialTooltip;

        // Editor Tools ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        // Main Header
        public static readonly string editorToolHeaderLabel = $"Editor Tools";
        public static readonly string editorToolHeaderTooltip =
            $"This is a collection of nifty editor tools which you can use to speed up your workflow." + "\n" +
            "\n" +
            $"They are enabled with the use of “Script Define Symbols” in:" + "\n" +
            "\n" +
            $"Project Settings → Player → Other Settings → Script Compilation → Script Define Symbols." + "\n" +
            "\n" +
            $"This way they can be bound to default hotkeys and they won’t hog up your toolbars if you don’t use them." + EditorTrial.trialTooltip;

        // Reference Finder
        public static readonly string editorToolReferenceFinderEnableLabel = $"Reference Finder";
        public static readonly string editorToolReferenceFinderEnableTooltip =
            $"Reference Finder is an editor tool for finding where an asset is referenced by another asset." + "\n" +
            $"It is enabled with the use of the Script Define Symbol \"SONITY_ENABLE_EDITOR_TOOL_REFERENCE_FINDER\"." + "\n" +
             $"This is assigned in:" + "\n" +
            "\n" +
            $"Project Settings → Player → Other Settings → Script Compilation → Script Define Symbols." + "\n" +
            "\n" +
            $"\"Find References for Selected Assets\" is useful for e.g. figuring out to which (if any) prefabs a {NameOf.SoundEvent} is assigned to." + "\n" +
            $"\"Open Reference Finder Window\" is useful for figuring out e.g. which AudioClips are completely unreferenced and therefore unused." + "\n" +
            $"Tip: Close the window before renaming or moving any file in the project or it might lag a lot." + "\n" +
            "\n" +
            $"Toolbar Actions" + "\n" +
            $"Tools/Sonity Tools 🛠/Reference Finder 🔍/Open Reference Finder Window" + "\n" +
            $"Tools/Sonity Tools 🛠/Reference Finder 🔍/Find References for Selected Assets" + "\n" +
            $"Assets/Sonity Tools 🛠/Reference Finder 🔍/Open Reference Finder Window" + "\n" +
            $"Assets/Sonity Tools 🛠/Reference Finder 🔍/Find References for Selected Assets" + "\n" +
            "\n" +
            $"Default Shortcuts" + "\n" +
            $"Ctrl+Shift+Alt+F - Reference Finder 🔍/Find References for Selected Assets" + "\n" +
            "\n" +
            $"Reference Finder is made by Alexey Perov and licensed under a free to use and distribute MIT license." + EditorTrial.trialTooltip;

        public static readonly string editorToolReferenceFinderAddLabel = $"Add Scripting Define Symbol:\n\"SONITY_ENABLE_EDITOR_TOOL_\nREFERENCE_FINDER\"";
        public static readonly string editorToolReferenceFinderAddTooltip =
            $"Pressing on this button will automatically add the Scripting Define Symbol \"SONITY_ENABLE_EDITOR_TOOL_REFERENCE_FINDER\" if it doesn't exist." + "\n" +
            "\n" +
            $"" + EditorTrial.trialTooltip;

        public static readonly string editorToolReferenceFinderRemoveLabel = $"Remove Scripting Define Symbol:\n\"SONITY_ENABLE_EDITOR_TOOL_\nREFERENCE_FINDER\"";
        public static readonly string editorToolReferenceFinderRemoveTooltip =
            $"Pressing on this button will automatically remove the Scripting Define Symbol \"SONITY_ENABLE_EDITOR_TOOL_REFERENCE_FINDER\" if it exists." + "\n" +
            "\n" +
            $"" + EditorTrial.trialTooltip;

        // Select Same Type
        public static readonly string editorToolSelectSameTypeEnableLabel = $"Select Same Type";
        public static readonly string editorToolSelectSameTypeEnableTooltip =
            $"Select Same Type is an editor tool for quickly selecting all assets of the same type in a folder which enables you to quickly edit a lot of assets." + "\n" +
            $"It is enabled with the use of the Script Define Symbol \"SONITY_ENABLE_EDITOR_TOOL_SELECT_SAME_TYPE\"." + "\n" +
             $"This is assigned in:" + "\n" +
            "\n" +
            $"Project Settings → Player → Other Settings → Script Compilation → Script Define Symbols." + "\n" +
            "\n" +
            $"Toolbar Actions" + "\n" +
            $"Tools/Sonity Tools 🛠/Select Same Type 🤏/In Same Folder" + "\n" +
            $"Tools/Sonity Tools 🛠/Select Same Type 🤏/In Subfolders" + "\n" +
            $"Assets/Sonity Tools 🛠/Select Same Type 🤏/In Same Folder" + "\n" +
            $"Assets/Sonity Tools 🛠/Select Same Type 🤏/In Subfolders" + "\n" +
            "\n" +
            $"Default Shortcuts" + "\n" +
            $"Ctrl+Alt+A - Select Same Type 🤏/In Same Folder" + "\n" +
            $"Ctrl+Alt+Shift+A - Select Same Type 🤏/In Subfolders" + EditorTrial.trialTooltip;

        public static readonly string editorToolSelectSameTypeAddLabel = $"Add Scripting Define Symbol:\n\"SONITY_ENABLE_EDITOR_TOOL_\nSELECT_SAME_TYPE\"";
        public static readonly string editorToolSelectSameTypeAddTooltip =
            $"Pressing on this button will automatically add the Scripting Define Symbol \"SONITY_ENABLE_EDITOR_TOOL_SELECT_SAME_TYPE\" if it doesn't exist." + "\n" +
            "\n" +
            $"" + EditorTrial.trialTooltip;

        public static readonly string editorToolSelectSameTypeRemoveLabel = $"Remove Scripting Define Symbol:\n\"SONITY_ENABLE_EDITOR_TOOL_\nSELECT_SAME_TYPE\"";
        public static readonly string editorToolSelectSameTypeRemoveTooltip =
            $"Pressing on this button will automatically remove the Scripting Define Symbol \"SONITY_ENABLE_EDITOR_TOOL_SELECT_SAME_TYPE\" if it exists." + "\n" +
            "\n" +
            $"" + EditorTrial.trialTooltip;

        // Selection History
        public static readonly string editorToolSelectionHistoryEnableLabel = $"Selection History";
        public static readonly string editorToolSelectionHistoryEnableTooltip =
            $"Selection History is an editor tool for quickly undoing and redoing selections." + "\n" +
            $"This enables you to quickly move between objects you’ve previously selected." + "\n" +
            $"It is enabled with the use of the Script Define Symbol \"SONITY_ENABLE_EDITOR_TOOL_SELECTION_HISTORY\"." + "\n" +
            $"This is assigned in:" + "\n" +
            "\n" +
            $"Project Settings → Player → Other Settings → Script Compilation → Script Define Symbols." + "\n" +
            "\n" +
            $"Toolbar Actions" + "\n" +
            $"Tools/Sonity Tools 🛠/Selection History 📜/Back" + "\n" +
            $"Tools/Sonity Tools 🛠/Selection History 📜/Forward" + "\n" +
            $"Assets/Sonity Tools 🛠/Selection History 📜/Back" + "\n" +
            $"Assets/Sonity Tools 🛠/Selection History 📜/Forward" + "\n" +
            "\n" +
            $"Default Shortcuts" + "\n" +
            $"U - Selection History 📜/Back" + "\n" +
            $"Shift+U - Selection History 📜/Forward" + "\n" +
            "\n" +
            $"Selection History is made by Matthew Miner and licensed under a free to use and distribute MIT license." + EditorTrial.trialTooltip;

        public static readonly string editorToolSelectionHistoryAddLabel = $"Add Scripting Define Symbol:\n\"SONITY_ENABLE_EDITOR_TOOL_\nSELECTION_HISTORY\"";
        public static readonly string editorToolSelectionHistoryAddTooltip =
            $"Pressing on this button will automatically add the Scripting Define Symbol \"SONITY_ENABLE_EDITOR_TOOL_SELECTION_HISTORY\" if it doesn't exist." + "\n" +
            "\n" +
            $"" + EditorTrial.trialTooltip;

        public static readonly string editorToolSelectionHistoryRemoveLabel = $"Remove Scripting Define Symbol:\n\"SONITY_ENABLE_EDITOR_TOOL_\nSELECTION_HISTORY\"";
        public static readonly string editorToolSelectionHistoryRemoveTooltip =
            $"Pressing on this button will automatically remove the Scripting Define Symbol \"SONITY_ENABLE_EDITOR_TOOL_SELECTION_HISTORY\" if it exists." + "\n" +
            "\n" +
            $"" + EditorTrial.trialTooltip;

        // All Tools
        public static readonly string editorToolsAddAllLabel = "Add All Tools";
        public static readonly string editorToolsAddAllTooltip = "Adds all editor tools scripting define symbols." + EditorTrial.trialTooltip;

        public static readonly string editorToolsRemoveAllLabel = "Remove All Tools";
        public static readonly string editorToolsRemoveAllTooltip = "Adds all editor tools scripting define symbols." + EditorTrial.trialTooltip;

        // Debug ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public static readonly string debugExpandLabel = "Debug";
        public static readonly string debugExpandTooltip = "Debug of Sonity." + EditorTrial.trialTooltip;

        public static readonly string debugAudioListenerLabel = $"Audio Listener";
        public static readonly string debugAudioListenerTooltip =
            $"Here you can see and select the currently cached {nameof(AudioListener)} in runtime." + "\n" +
            "\n" +
            $"If you click on the field your selection will show you the currently used {nameof(AudioListener)}." + "\n" +
            "\n" +
            $"The {nameof(AudioListener)} component is found and cached automatically and cannot be assigned here." + EditorTrial.trialTooltip;

        public static readonly string debugAudioListenerDistanceLabel = $"Audio Listener Distance";
        public static readonly string debugAudioListenerDistanceTooltip =
            $"Here you can see and select the currently cached {NameOf.AudioListenerDistance} in runtime." + "\n" +
            "\n" +
            $"If you click on the field your selection will show you the currently used {NameOf.AudioListenerDistance}." + "\n" +
            "\n" +
            $"The {NameOf.AudioListenerDistance} component is found and cached automatically and cannot be assigned here." + "\n" +
            "\n" +
            $"Is only shown if \"" + overrideListenerDistanceLabel + "\" is enabled." + EditorTrial.trialTooltip;

        // Enable Disable All Debug /////////////////////////////////////////////////////////////////////////////

        public static readonly string editorPrefsDebugAllDisableLabel = $"Local - Disable All Debug";
        public static readonly string editorPrefsDebugAllEnableLabel = $"Local - Enable All Debug";
        public static readonly string editorPrefsDebugAllTooltip =
            $"Uses the EditorPrefs to save the value instead of in the {NameOf.SoundManager} component." + "\n" +
            "\n" +
            $"This is useful because EditorPrefs are local settings stored locally per computer." + "\n" +
            "\n" +
            $"Which means that you can't accidentally commit Debug Enable to the version control." + "\n" +
            "\n" +
            $"The default value is false, so it won't debug for others unless \"Force Debug\" is enabled." + "\n" +
            "\n" +
            $"Because the setting set with EditorPrefs it will be the same in all Sonity projects on the same computer." + EditorTrial.trialTooltip;

        public static readonly string overrideLocalDebugLabel = $"Override Local Debug";
        public static readonly string overrideLocalDebugTooltip =
            $"Overrides the local debug to be force enabled." + "\n" +
            "\n" +
            $"Useful if you have an e.g. debug scene where everyone should see the debugs." + EditorTrial.trialTooltip;

        // Log SoundEvents /////////////////////////////////////////////////////////////////////////////

        public static readonly string logSoundEventsHeaderLabel = $"Log SoundEvents";
        public static readonly string logSoundEventsHeaderTooltip =
            $"When enabled {NameOf.SoundEvent} actions will be logged to the console." + "\n" +
            "\n" +
            $"This is useful to debug what happens with {NameOf.SoundEvent}s during runtime." + "\n" +
            "\n" +
            $"It also enables you to see where in the code the {NameOf.SoundEvent} is played/stopped from if you enable Stack Tracke Logging “Script Only”." + EditorTrial.trialTooltip;

        public static readonly string logSoundEventsEnableLabel = $"To Console";
        public static readonly string logSoundEventsEnableTooltip =
            $"If enabled it will log {NameOf.SoundEvent} actions to the console." + EditorTrial.trialTooltip;

        public static readonly string logSoundEventsSelectObjectLabel = $"Click On Log Selects";
        public static readonly string logSoundEventsSelectObjectTooltip =
            $"If you click the log in the console, you will be redirected to one of either object:" + "\n" +
            "\n" +
            $"Owner" + "\n" +
            $"Selects the owner transform which is used to play the {NameOf.SoundEvent}." + "\n" +
            "\n" +
            $"Position" + "\n" +
            $"Selects the transform position which is used to play the {NameOf.SoundEvent} if its played with PlayAtPosition with a Transform." + "\n" +
            "\n" +
            $"SoundEvent" + "\n" +
            $"Selects the {NameOf.SoundEvent} which is logged." + "\n" +
            "\n" +
            $"If an Owner or Position can't be found (for example when logging stop etc) it will redirect to the {NameOf.SoundEvent} instead." + EditorTrial.trialTooltip;

        public static readonly string logSoundEventsLogTypeLabel = $"Debug Log Type";
        public static readonly string logSoundEventsLogTypeTooltip =
            $"Select which kind of Debug.Log you want to use." + "\n" +
            "\n" +
            $"Switch between either Debug.Log, Debug.LogWarning or Debug.LogError." + EditorTrial.trialTooltip;

        public static readonly string logSoundEventsSettingsLabel = $"Log Settings";
        public static readonly string logSoundEventsSettingsTooltip =
            $"Opens a dropdown menu where you can select which events should be logged." + "\n" +
            "\n" +
            $"SoundEvent Play" + "\n" +
            $"Logs when a {NameOf.SoundEvent} is played." + "\n" +
            "\n" +
            $"SoundEvent Stop" + "\n" +
            $"Logs when a {NameOf.SoundEvent} is stopped with a stop command." + "\n" +
            "\n" +
            $"SoundEvent Pool" + "\n" +
            $"Logs when a {NameOf.SoundEvent} is pooled after its stopped." + "\n" +
            "\n" +
            $"SoundEvent Pause" + "\n" +
            $"Logs when a {NameOf.SoundEvent} is paused." + "\n" +
            "\n" +
            $"SoundEvent Unpause" + "\n" +
            $"Logs when a {NameOf.SoundEvent} is unpaused." + "\n" +
            "\n" +
            $"SoundEvent Global Pause" + "\n" +
            $"Logs when a {NameOf.SoundEvent} is paused using global pause." + "\n" +
            "\n" +
            $"SoundEvent Global Unpause" + "\n" +
            $"Logs when a {NameOf.SoundEvent} is unpaused using global unpause." + "\n" +
            "\n" +
            $"SoundParameters Once" + "\n" +
            $"Logs any {NameOf.SoundParameter} set to UpdateMode Once passed when playing a {NameOf.SoundEvent}." + "\n" +
            "\n" +
            $"SoundParameters Continuous" + "\n" +
            $"Logs any {NameOf.SoundParameter} set to UpdateMode Continuous on an active {NameOf.SoundEvent}." + EditorTrial.trialTooltip;

        public static readonly string logSoundEventsResetLabel = "Reset";
        public static readonly string logSoundEventsResetTooltip = "Resets Log Settings." + EditorTrial.trialTooltip;

        // Draw SoundEvents /////////////////////////////////////////////////////////////////////////////

        public static readonly string drawSoundEventsLabel = $"Draw SoundEvents";
        public static readonly string drawSoundEventsTooltip =
            $"Draws the names of all currently playing {NameOf.SoundEvent} in the scene and/or game view." + "\n" +
            "\n" +
            $"Useful for debugging when you want to see what is playing and where." + EditorTrial.trialTooltip;

        public static readonly string drawSoundEventsInSceneViewEnabledLabel = $"In Scene View";
        public static readonly string drawSoundEventsInSceneViewEnabledTooltip =
            $"Draws debug names in the Unity scene view." + "\n" +
            "\n" +
            $"Doesn't work in Unity versions older than 2019.1." + EditorTrial.trialTooltip;

        public static readonly string drawSoundEventsInGameViewEnabledLabel = $"In Game View";
        public static readonly string drawSoundEventsInGameViewEnabledTooltip =
            $"Draws debug names in the Unity game view." + "\n" +
            "\n" +
            $"Only applied in the Unity editor." + EditorTrial.trialTooltip;

        public static readonly string drawSoundEventsHideIfCloserThanLabel = $"Hide if Closer Than";
        public static readonly string drawSoundEventsHideIfCloserThanTooltip =
            $"Hides the debug if its closer to the camera than the allowed value in Game view." + "\n" +
            "\n" +
            $"Useful for hiding {NameOf.SoundEvent}s which are played on the camera which you don't want to see." + EditorTrial.trialTooltip;

        public static readonly string drawSoundEventsFontSizeLabel = $"Font Size";
        public static readonly string drawSoundEventsFontSizeTooltip = "The font size of the text." + EditorTrial.trialTooltip;

        public static readonly string drawSoundEventsVolumeToOpacityLabel = "Volume to Opacity";
        public static readonly string drawSoundEventsVolumeToOpacityTooltip =
            $"How much of the volume of the {NameOf.SoundEvent} will be applied to the transparency of the text." + "\n" +
            "\n" +
            $"E.g lower volumes will be more transparent." + EditorTrial.trialTooltip;

        public static readonly string drawSoundEventsLifetimeToOpacityLabel = "Lifetime to Opacity";
        public static readonly string drawSoundEventsLifetimeToOpacityTooltip =
            $"How much the lifetime of the {NameOf.SoundEvent} will affect the transparency of the text." + EditorTrial.trialTooltip;

        public static readonly string drawSoundEventsLifetimeFadeLengthLabel = "Lifetime Fade Length";
        public static readonly string drawSoundEventsLifetimeFadeLengthTooltip =
            $"How long the fade should be." + "\n" +
            "\n" +
            $"Is calculated using the time scale selected in the {NameOf.SoundManager}." + EditorTrial.trialTooltip;

        public static readonly string drawSoundEventsColorStartLabel = "Start Color";
        public static readonly string drawSoundEventsColorStartTooltip = "The color the text should have when it starts playing." + EditorTrial.trialTooltip;

        public static readonly string drawSoundEventsColorEndLabel = "End Color";
        public static readonly string drawSoundEventsColorEndTooltip = "Which color the text should fade to over the lifetime." + EditorTrial.trialTooltip;

        public static readonly string drawSoundEventsColorOutlineLabel = "Outline Color";
        public static readonly string drawSoundEventsColorOutlineTooltip = "The color of the text outline." + EditorTrial.trialTooltip;

        public static readonly string drawSoundEventsResetLabel = $"Reset Style";
        public static readonly string drawSoundEventsResetTooltip = $"Resets style of drawing {NameOf.SoundEvent}s." + EditorTrial.trialTooltip;

        // Reset All Debug
        public static readonly string debugResetAllLabel = $"Reset All Debug";
        public static readonly string debugResetAllTooltip = $"Resets All Debug Settings." + EditorTrial.trialTooltip;

        // Statistics ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        public static readonly string statisticsExpandLabel = "Statistics";
        public static readonly string statisticsExpandTooltip = "Statistics of Sonity." + EditorTrial.trialTooltip;

        public static readonly string statisticsOverallLabel = "General";
        public static readonly string statisticsOverallTooltip = "General Statistics of Sonity." + EditorTrial.trialTooltip;

        public static readonly string statisticsInstanceLabel = "Instances";
        public static readonly string statisticsInstanceTooltip = $"{NameOf.SoundEvent} Instance Statistics" + EditorTrial.trialTooltip;

        // SoundEvent Instances
        public static readonly string statisticsSoundEventsLabel = $"{NameOf.SoundEvent} Instances";
        public static readonly string statisticsSoundEventsTooltip = $"Statistics of {NameOf.SoundEvent} Instances." + EditorTrial.trialTooltip;
        public static readonly string statisticsSoundEventsActiveTooltip = $"The number of active {NameOf.SoundEvent} Instances." + EditorTrial.trialTooltip;
        public static readonly string statisticsSoundEventsInactiveTooltip = $"The number of inactive {NameOf.SoundEvent} Instances." + EditorTrial.trialTooltip;
        public static readonly string statisticsSoundEventsTotalTooltip = $"The total number of created {NameOf.SoundEvent} Instances." + EditorTrial.trialTooltip;

        // Voices
        public static readonly string statisticsVoicesLabel = "Voices";
        public static readonly string statisticsVoicesTooltip = "Statistics of Voices." + EditorTrial.trialTooltip;

        public static readonly string statisticsVoicesActiveTooltip = "The number of Voices playing audio." + EditorTrial.trialTooltip;
        public static readonly string statisticsVoicesInactiveTooltip = "The number of inactive Voices." + EditorTrial.trialTooltip;
        public static readonly string statisticsVoicesTotalTooltip = "The number of created Voices." + EditorTrial.trialTooltip;

        public static readonly string statisticsVoicesPausedTooltip = "The number of waiting or paused Voices in the pool.\n" +
            "(It is more efficient to pause voices instead of stopping them directly)" + EditorTrial.trialTooltip;
        public static readonly string statisticsVoicesStoppedTooltip = "The number of stopped Voices in the pool." + EditorTrial.trialTooltip;
        public static readonly string statisticsVoicesStolenTooltip = "The number of stolen Voices since start." + EditorTrial.trialTooltip;

        public static readonly string statisticsVoicesPlayedTooltip = "The number of played Voices since start." + EditorTrial.trialTooltip;
        public static readonly string statisticsVoicesMaxSimultaneousTooltip = "The maximum number of simultaneously playing Voices since start." + EditorTrial.trialTooltip;

        // Voice Effects
        public static readonly string statisticsVoiceEffectsLabel = "Voice Effects";
        public static readonly string statisticsVoiceEffectsTooltip =
            "Statistics of Voice Effects." + "\n" +
            "\n" +
            "A Voice with any combination of waveshaper/lowpass/highpass counts as one Voice Effect." + EditorTrial.trialTooltip;

        public static readonly string statisticsVoiceEffectsActiveTooltip = "The number of active Voice Effects." + EditorTrial.trialTooltip;
        public static readonly string statisticsVoiceEffectsAvailableTooltip = "How many Voice Effects are available." + EditorTrial.trialTooltip;
        public static readonly string statisticsVoiceEffectsTotalTooltip = "How many Voice Effects can exist simultaneously." + EditorTrial.trialTooltip;

        public static readonly string statisticsSoundEventInstancesLabel = "Instance Statistics";
        public static readonly string statisticsSoundEventInstancesTooltip =
            $"Real-time statistics per {NameOf.SoundEvent} Instance." + "\n" +
            "\n" +
            "Available in Playmode." + EditorTrial.trialTooltip;

        public static readonly string statisticsSortingLabel = $"Sort By";
        public static readonly string statisticsSortingTooltip =
            $"Which method to sort the list of {NameOf.SoundEvent} Instances." + "\n" +
            "\n" +
            "Name" + "\n" +
            "Sorts by alphabetical order." + "\n" +
            "\n" +
            "Voices" + "\n" +
            "Sorts by voice count." + "\n" +
            "\n" +
            "Plays" + "\n" +
            "Sorts by number of plays." + "\n" +
            "\n" +
            "Volume" + "\n" +
            "Sorts by volume." + "\n" +
            "\n" +
            "Time" + "\n" +
            "Sorts by last time played." + EditorTrial.trialTooltip;

        public static readonly string statisticsShowLabel = $"Show";
        public static readonly string statisticsShowTooltip =
            $"Toggle what information to show about the {NameOf.SoundEvent} Instances." + "\n" +
            "\n" +
            "Show Active" + "\n" +
            "How many are currently active." + "\n" +
            "\n" +
            "Show Disabled" + "\n" +
            "How many are currently disabled." + "\n" +
            "\n" +
            "Show Voices" + "\n" +
            "How many voices are currently used." + "\n" +
            "\n" +
            "Show Plays" + "\n" +
            "The number of total plays." + "\n" +
            "\n" +
            "Show Volume" + "\n" +
            "The current average volume." + EditorTrial.trialTooltip;

        public static readonly string statisticsInstancesResetLabel = "Reset";
        public static readonly string statisticsInstancesResetTooltip = "Resets statistics." + EditorTrial.trialTooltip;

        public static readonly string statisticsAllResetLabel = "Reset All Statistics";
        public static readonly string statisticsAllResetTooltip = "Resets all statistics settings." + EditorTrial.trialTooltip;
    }
}
#endif