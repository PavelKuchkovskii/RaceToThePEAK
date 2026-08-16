using PeakRace.Patch;
using System;

namespace PeakRace.Core;

/// <summary>
/// Stable progression identity for an explicitly selected team. Players who
/// did not select a team intentionally receive an actor-scoped identity.
/// </summary>
internal readonly struct RaceTeamScope : IEquatable<RaceTeamScope>
{
    private RaceTeamScope(bool isExplicitTeam, int value)
    {
        IsExplicitTeam = isExplicitTeam;
        Value = value;
    }

    internal bool IsExplicitTeam { get; }
    internal int Value { get; }

    internal string PropertySuffix => IsExplicitTeam
        ? $"Team.{Value}"
        : $"Actor.{Value}";

    internal static RaceTeamScope ForCharacter(Character character)
    {
        CharacterTeamInfo teamInfo = character != null
            ? character.GetComponent<CharacterTeamInfo>()
            : null;
        if (teamInfo != null && teamInfo.HasExplicitTeam)
        {
            return new RaceTeamScope(isExplicitTeam: true, teamInfo.teamInt);
        }

        int actorNumber = character?.photonView?.Owner?.ActorNumber
            ?? character?.photonView?.ViewID
            ?? character?.GetInstanceID()
            ?? 0;
        return new RaceTeamScope(isExplicitTeam: false, actorNumber);
    }

    internal bool Contains(Character character)
    {
        return Equals(ForCharacter(character));
    }

    public bool Equals(RaceTeamScope other)
    {
        return IsExplicitTeam == other.IsExplicitTeam && Value == other.Value;
    }

    public override bool Equals(object obj)
    {
        return obj is RaceTeamScope other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return ((IsExplicitTeam ? 1 : 0) * 397) ^ Value;
        }
    }

    public override string ToString()
    {
        return IsExplicitTeam ? $"team {Value}" : $"solo actor {Value}";
    }
}
