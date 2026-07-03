using System.Collections.Generic;

public sealed class RhythmPattern
{
	public RhythmPattern(List<HitType> hits, Enemy ownerEnemy)
	{
		Hits = hits ?? new List<HitType>();
		OwnerEnemy = ownerEnemy;
	}

	public List<HitType> Hits { get; }
	public Enemy OwnerEnemy { get; }
}
