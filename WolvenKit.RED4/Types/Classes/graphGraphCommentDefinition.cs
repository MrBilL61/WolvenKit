namespace WolvenKit.RED4.Types
{
	/// <summary>
	/// Editor-only type used for comment/annotation nodes in the graph view.
	/// These nodes have no sockets and are not serialized into the game's graph format.
	/// Layout and text are stored in GraphEditorStates JSON.
	/// </summary>
	public partial class graphGraphCommentDefinition : graphIGraphObjectDefinition
	{
		[Ordinal(0)]
		[RED("comment")]
		public CString Comment
		{
			get => GetPropertyValue<CString>();
			set => SetPropertyValue<CString>(value);
		}

		public graphGraphCommentDefinition()
		{
			PostConstruct();
		}

		partial void PostConstruct();
	}
}
