using System;
using System.Collections.Generic;

public enum RudimentId
{
	SingleStroke,
	DoubleStroke,
	Paradiddle,
	ParadiddleDiddle,
	FlamAccent,
	DragLike,
	AccentGrid,
	InvertedParadiddle,
	HertaLike,
	SyncopatedTriplet
}

public readonly struct RudimentInfo
{
	public RudimentInfo(RudimentId id, string displayName, int difficulty, string role)
	{
		Id = id;
		DisplayName = displayName;
		Difficulty = difficulty;
		Role = role;
	}

	public RudimentId Id { get; }
	public string DisplayName { get; }
	public int Difficulty { get; }
	public string Role { get; }
}

public static class RudimentLibrary
{
	private static readonly Dictionary<RudimentId, RudimentInfo> Infos = new Dictionary<RudimentId, RudimentInfo>
	{
		{ RudimentId.SingleStroke, new RudimentInfo(RudimentId.SingleStroke, "Single Stroke", 1, "straight time") },
		{ RudimentId.DoubleStroke, new RudimentInfo(RudimentId.DoubleStroke, "Double Stroke", 2, "roll feel") },
		{ RudimentId.Paradiddle, new RudimentInfo(RudimentId.Paradiddle, "Paradiddle", 3, "hand shift") },
		{ RudimentId.ParadiddleDiddle, new RudimentInfo(RudimentId.ParadiddleDiddle, "Paradiddle-diddle", 3, "six-note phrase") },
		{ RudimentId.FlamAccent, new RudimentInfo(RudimentId.FlamAccent, "Flam Accent", 4, "accent phrase") },
		{ RudimentId.DragLike, new RudimentInfo(RudimentId.DragLike, "Drag", 4, "pickup accent") },
		{ RudimentId.AccentGrid, new RudimentInfo(RudimentId.AccentGrid, "Accent Grid", 3, "dynamic control") },
		{ RudimentId.InvertedParadiddle, new RudimentInfo(RudimentId.InvertedParadiddle, "Inverted Paradiddle", 4, "inside-out hand shift") },
		{ RudimentId.HertaLike, new RudimentInfo(RudimentId.HertaLike, "Herta", 5, "burst grouping") },
		{ RudimentId.SyncopatedTriplet, new RudimentInfo(RudimentId.SyncopatedTriplet, "Sync Triplet", 5, "triplet push") }
	};

	private static readonly Dictionary<RudimentId, HitType[]> Patterns = new Dictionary<RudimentId, HitType[]>
	{
		// R L R L / L R L R: the basic alternating hand-to-hand feel.
		{ RudimentId.SingleStroke, new[] { HitType.Left, HitType.Right, HitType.Left, HitType.Right } },

		// R R L L: useful for "roll" enemies without adding new buttons.
		{ RudimentId.DoubleStroke, new[] { HitType.Left, HitType.Left, HitType.Right, HitType.Right } },

		// R L R R / L R L L. This is the main MVP musical identity pattern.
		{ RudimentId.Paradiddle, new[] { HitType.Left, HitType.Right, HitType.Left, HitType.Left, HitType.Right, HitType.Left, HitType.Right, HitType.Right } },

		// R L R R L L: a compact six-note rudiment that creates a phrase over the bar.
		{ RudimentId.ParadiddleDiddle, new[] { HitType.Left, HitType.Right, HitType.Left, HitType.Left, HitType.Right, HitType.Right } },

		// Flam is represented as an accented primary hit in MVP, not as a grace-note chord.
		{ RudimentId.FlamAccent, new[] { HitType.AccentLeft, HitType.Right, HitType.Left, HitType.AccentRight, HitType.Left, HitType.Right } },

		// A drag-like feel: small repeated hand into an accented answer.
		{ RudimentId.DragLike, new[] { HitType.Left, HitType.Left, HitType.AccentRight, HitType.Right, HitType.Left, HitType.AccentRight } },

		// Accent grid: same hand pattern, different musical weight.
		{ RudimentId.AccentGrid, new[] { HitType.AccentLeft, HitType.Right, HitType.Left, HitType.Right, HitType.Left, HitType.AccentRight, HitType.Left, HitType.Right } },

		// Inside-out phrasing that feels less predictable on the grid.
		{ RudimentId.InvertedParadiddle, new[] { HitType.Left, HitType.Left, HitType.Right, HitType.Left, HitType.Right, HitType.Right, HitType.Left, HitType.Right } },

		// Burst-oriented four-note cell that creates a denser push.
		{ RudimentId.HertaLike, new[] { HitType.Left, HitType.Right, HitType.Right, HitType.Right, HitType.Right, HitType.Left, HitType.Left, HitType.Left } },

		// Triplet-style grouping flattened into the current step grid.
		{ RudimentId.SyncopatedTriplet, new[] { HitType.AccentLeft, HitType.Right, HitType.Left, HitType.AccentRight, HitType.Left, HitType.Right } }
	};

	public static HitType[] Get(RudimentId id, bool mirror = false)
	{
		if (!Patterns.TryGetValue(id, out HitType[] pattern))
		{
			pattern = Patterns[RudimentId.SingleStroke];
		}

		HitType[] result = new HitType[pattern.Length];
		for (int i = 0; i < pattern.Length; i++)
		{
			result[i] = mirror ? Mirror(pattern[i]) : pattern[i];
		}

		return result;
	}

	public static RudimentInfo GetInfo(RudimentId id)
	{
		if (Infos.TryGetValue(id, out RudimentInfo info))
		{
			return info;
		}

		return Infos[RudimentId.SingleStroke];
	}

	public static string GetDisplayName(RudimentId id, bool mirror = false)
	{
		string name = GetInfo(id).DisplayName;
		return mirror ? $"{name} Mirror" : name;
	}

	public static string GetPhraseName(params (RudimentId Id, bool Mirror)[] parts)
	{
		if (parts == null || parts.Length == 0)
		{
			return GetDisplayName(RudimentId.SingleStroke);
		}

		List<string> names = new List<string>();
		foreach ((RudimentId id, bool mirror) in parts)
		{
			names.Add(GetDisplayName(id, mirror));
		}

		return string.Join(" + ", names);
	}

	public static string GetSparseVariantName(string baseName)
	{
		if (string.IsNullOrWhiteSpace(baseName))
		{
			return "Syncopated Phrase";
		}

		return $"{baseName} Pocket";
	}

	public static HitType[] Compose(params (RudimentId Id, bool Mirror)[] parts)
	{
		List<HitType> result = new List<HitType>();
		foreach ((RudimentId id, bool mirror) in parts)
		{
			result.AddRange(Get(id, mirror));
		}

		return result.ToArray();
	}

	public static HitType[] BuildGroove(RudimentId id, bool mirror, int slotCount, int[] accentSlots = null, int[] restSlots = null)
	{
		return BuildGrooveFromHits(Get(id, mirror), slotCount, accentSlots, restSlots);
	}

	public static HitType[] ComposeGroove(int slotCount, int[] accentSlots, int[] restSlots, params (RudimentId Id, bool Mirror)[] parts)
	{
		return BuildGrooveFromHits(Compose(parts), slotCount, accentSlots, restSlots);
	}

	public static HitType[] BuildGrooveFromHits(HitType[] source, int slotCount, int[] accentSlots = null, int[] restSlots = null)
	{
		HitType[] groove = RepeatToLength(source, slotCount);
		ApplyAccentSlots(groove, accentSlots);
		ApplyRestSlots(groove, restSlots);
		return groove;
	}

	public static HitType[] BuildShiftedGroove(RudimentId id, bool mirror, int slotCount, int rotationSteps, int[] accentSlots = null, int[] restSlots = null)
	{
		HitType[] groove = BuildGroove(id, mirror, slotCount, accentSlots, restSlots);
		return Rotate(groove, rotationSteps);
	}

	public static HitType[] BuildCompositePhrase(int slotCount, int[] accentSlots = null, int[] restSlots = null, params (RudimentId Id, bool Mirror, int Rotation)[] parts)
	{
		List<HitType> source = new List<HitType>();
		if (parts != null)
		{
			foreach ((RudimentId id, bool mirror, int rotation) in parts)
			{
				HitType[] phrase = Rotate(Get(id, mirror), rotation);
				source.AddRange(phrase);
			}
		}

		return BuildGrooveFromHits(source.ToArray(), slotCount, accentSlots, restSlots);
	}

	public static HitType[] AddSparseRests(HitType[] pattern, int everyNthSlot)
	{
		if (pattern == null || pattern.Length == 0 || everyNthSlot <= 1)
		{
			return pattern ?? Array.Empty<HitType>();
		}

		List<HitType> result = new List<HitType>();
		for (int i = 0; i < pattern.Length; i++)
		{
			result.Add(pattern[i]);
			bool canInsertRest = (i + 1) % everyNthSlot == 0 && i < pattern.Length - 1;
			if (canInsertRest)
			{
				result.Add(HitType.Rest);
			}
		}

		return result.ToArray();
	}

	private static HitType[] RepeatToLength(HitType[] source, int slotCount)
	{
		if (source == null || source.Length == 0)
		{
			source = Patterns[RudimentId.SingleStroke];
		}

		int count = Math.Max(1, slotCount);
		HitType[] result = new HitType[count];
		for (int i = 0; i < count; i++)
		{
			result[i] = source[i % source.Length];
		}

		return result;
	}

	private static HitType[] Rotate(HitType[] source, int steps)
	{
		if (source == null || source.Length == 0)
		{
			return Array.Empty<HitType>();
		}

		HitType[] result = new HitType[source.Length];
		int shift = ((steps % source.Length) + source.Length) % source.Length;
		for (int i = 0; i < source.Length; i++)
		{
			int sourceIndex = (i + shift) % source.Length;
			result[i] = source[sourceIndex];
		}

		return result;
	}

	private static void ApplyAccentSlots(HitType[] groove, int[] accentSlots)
	{
		if (groove == null || accentSlots == null)
		{
			return;
		}

		foreach (int slot in accentSlots)
		{
			if (slot < 0 || slot >= groove.Length || groove[slot] == HitType.Rest)
			{
				continue;
			}

			groove[slot] = Accent(groove[slot]);
		}
	}

	private static void ApplyRestSlots(HitType[] groove, int[] restSlots)
	{
		if (groove == null || restSlots == null)
		{
			return;
		}

		int maxRestCount = Math.Max(1, groove.Length / 4);
		foreach (int slot in restSlots)
		{
			if (slot <= 0 || slot >= groove.Length - 1 || CountRests(groove) >= maxRestCount)
			{
				continue;
			}

			if (IsAccent(groove[slot]) || groove[slot - 1] == HitType.Rest || groove[slot + 1] == HitType.Rest)
			{
				continue;
			}

			groove[slot] = HitType.Rest;
		}
	}

	private static int CountRests(HitType[] groove)
	{
		int count = 0;
		if (groove == null)
		{
			return count;
		}

		foreach (HitType hit in groove)
		{
			if (hit == HitType.Rest)
			{
				count++;
			}
		}

		return count;
	}

	private static bool IsAccent(HitType hit)
	{
		return hit == HitType.AccentLeft || hit == HitType.AccentRight;
	}

	private static HitType Accent(HitType hit)
	{
		return hit switch
		{
			HitType.Left => HitType.AccentLeft,
			HitType.Right => HitType.AccentRight,
			_ => hit
		};
	}

	private static HitType Mirror(HitType hit)
	{
		return hit switch
		{
			HitType.Left => HitType.Right,
			HitType.Right => HitType.Left,
			HitType.AccentLeft => HitType.AccentRight,
			HitType.AccentRight => HitType.AccentLeft,
			_ => hit
		};
	}
}
