using System.Windows;
using WolvenKit.RED4.Types;
using Point = System.Windows.Point;

namespace WolvenKit.App.ViewModels.GraphEditor;

/// <summary>
/// View model for a comment/annotation node in the graph. Comment nodes have no sockets
/// and are not part of the game's graph data; they are editor-only and stored in layout state.
/// </summary>
public sealed class CommentNodeViewModel : NodeViewModel
{
    private readonly uint _uniqueId;

    public override uint UniqueId => _uniqueId;

    public CommentNodeViewModel(graphGraphCommentDefinition data, uint uniqueId, Point location)
        : base(data)
    {
        _uniqueId = uniqueId;
        Location = location;
        Title = string.IsNullOrEmpty(data.Comment) ? "Comment" : data.Comment;
        Size = new Size(180, 60);
        // Input and Output remain empty; no connectors
    }

    internal override void GenerateSockets()
    {
        // Comment nodes have no sockets
        Input.Clear();
        Output.Clear();
    }

    protected override void UpdateTitle()
    {
        if (Data is graphGraphCommentDefinition commentData && !string.IsNullOrEmpty(commentData.Comment))
        {
            Title = commentData.Comment;
        }
        else
        {
            Title = "Comment";
        }
    }

    public override void RefreshFromData()
    {
        base.RefreshFromData();
        OnPropertyChanged(nameof(CommentText));
    }

    /// <summary>
    /// Comment text for display and persistence.
    /// </summary>
    public string CommentText
    {
        get => (Data as graphGraphCommentDefinition)?.Comment ?? string.Empty;
        set
        {
            if (Data is graphGraphCommentDefinition commentData)
            {
                commentData.Comment = value ?? string.Empty;
                Title = string.IsNullOrEmpty(value) ? "Comment" : value;
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(CommentText));
            }
        }
    }
}
