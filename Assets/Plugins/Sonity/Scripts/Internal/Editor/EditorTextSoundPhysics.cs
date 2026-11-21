// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

#if UNITY_EDITOR

using UnityEngine;

namespace Sonity.Internal {

    public class EditorTextSoundPhysics {

        public static readonly string soundPhysicsTooltip =
            $"{NameOf.SoundPhysics} is a component used for easily playing {NameOf.SoundEvent}s on physics interactions." + "\n" +
            "\n" +
            $"{NameOf.SoundPhysics} is split up into 3D/2D versions with and without friction because of performance reasons." + "\n" +
            "\n" +
            $"If friction sounds aren’t needed and performance is a priority then use the {NameOf.SoundPhysics} with no friction." + "\n" +
            "\n" +
            $"A {nameof(Rigidbody)} or {nameof(Rigidbody2D)} is required on this object." + "\n" +
            "\n" +
            $"One or several {nameof(Collider)} or {nameof(Collider2D)} should be placed on this object or its children." + "\n" +
            "\n" +
            $"Use intensity record in the {NameOf.SoundEvent} for easy scaling of the velocity into a 0 to 1 range." + "\n" +
            "\n" +
            $"All {NameOf.SoundPhysics} components are multi-object editable." + EditorTrial.trialTooltip;

        public static readonly string warningRigidbody3D = $"{nameof(Rigidbody)} required";
        public static readonly string warningRigidbody2D = $"{nameof(Rigidbody2D)} required";

        public static readonly string impactHeaderLabel = $"Impact";
        public static readonly string impactHeaderTooltip =
            $"Is triggered when the object starts touching a collider." + "\n" +
            "\n" +
            $"OnCollision uses velocity from Collision.relativeVelocity.magnitude and the position of the Collision.contacts with the highest impulse.magnitude." + "\n" +
            "\n" +
            $"OnTrigger uses velocity from Rigidbody.velocity.magnitude." + EditorTrial.trialTooltip;

        public static readonly string impactPlayLabel = $"Play Impact";
        public static readonly string impactPlayTooltip = impactHeaderTooltip;

        public static readonly string frictionHeaderLabel = $"Friction";
        public static readonly string frictionHeaderTooltip =
            $"Is triggered when the object is continuously touching a collider." + "\n" +
            "\n" +
            $"Uses velocity from Rigidbody.velocity.magnitude." + EditorTrial.trialTooltip;

        public static readonly string frictionPlayLabel = $"Play Friction";
        public static readonly string frictionPlayTooltip = frictionHeaderTooltip;

        public static readonly string exitHeaderLabel = $"Exit";
        public static readonly string exitHeaderTooltip =
            $"Is triggered when the object stops touching a collider." + "\n" +
            "\n" +
            $"Uses velocity from Rigidbody.velocity.magnitude." + EditorTrial.trialTooltip;

        public static readonly string exitPlayLabel = $"Play Exit";
        public static readonly string exitPlayTooltip = exitHeaderTooltip;

        public static readonly string playOnLabel = $"Play On";
        public static readonly string playOnTooltip =
            $"Selects if the {NameOf.SoundEvent}s should be played when a OnCollision and/or OnTrigger event occurs." + "\n" +
            "\n" +
            $"OnTrigger is when you have a collider which is set to “Is Trigger”." + EditorTrial.trialTooltip;

        public static readonly string conditionsLabel = $"Conditions";
        public static readonly string conditionsTooltip =
            $"If enabled then any {NameOf.SoundPhysicsCondition} added will be used to decide if the physics interaction should play a {NameOf.SoundEvent} or not." + "\n" +
            "\n" +
            $"{NameOf.SoundPhysicsCondition}s can also be used for playing different sounds with a single {NameOf.SoundEvent} by assigning {NameOf.SoundTag}s in the {NameOf.SoundPhysicsCondition}s." + EditorTrial.trialTooltip;

        public static readonly string soundTagLabel = $"SoundTags";
        public static readonly string soundTagTooltip =
            $"If enabled then any {NameOf.SoundTag}s added will be sent when triggering the {NameOf.SoundEvent}." + "\n" +
            "\n" +
            $"If a {NameOf.SoundTag} is not null it will override any {NameOf.SoundTag}s which might be sent through the {NameOf.SoundPhysicsCondition}s." + "\n" +
            "\n" +
            $"{NameOf.SoundTag}s can be used for triggering different sounds through a single {NameOf.SoundEvent}." + EditorTrial.trialTooltip;

        // Reset
        public static readonly string resetSettingsLabel = $"Reset Settings";
        public static readonly string resetSettingsTooltip = $"Resets to default values without removing any objects." + EditorTrial.trialTooltip;

        public static readonly string resetAllLabel = $"Reset All";
        public static readonly string resetAllTooltip = $"Resets everything." + EditorTrial.trialTooltip;

    }
}
#endif