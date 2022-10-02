﻿namespace Nouns.Assets.Core
{
	public class AssetDetails
	{
		public string? FriendlyName { get; set; }
		public AssetClassification Classification { get; set; }
		public string? Path { get; set; }
		public object? Asset { get; set; }
	}
}