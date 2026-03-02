using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using VideoArchive.Models;
using VideoArchive.Services;

namespace VideoArchive.Views;

public sealed partial class TagsPanel : UserControl
{
    private readonly List<Tag> _tags = [];

    public TagsPanel()
    {
        this.InitializeComponent();

        // Default color: cornflower blue
        TagColorPicker.Color = Windows.UI.Color.FromArgb(255, 100, 149, 237);

        this.Loaded += async (_, _) =>
        {
            using var scope = App.Services.CreateScope();
            var tagService = scope.ServiceProvider.GetRequiredService<ITagService>();
            var all = await tagService.GetAllAsync();
            _tags.Clear();
            _tags.AddRange(all);
            RefreshList();
        };
    }

    private void RefreshList()
    {
        TagsList.Items.Clear();

        if (_tags.Count == 0)
        {
            TagsList.Items.Add(new TextBlock
            {
                Text = "No tags yet — add one below.",
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                Margin = new Thickness(0, 8, 0, 8),
            });
            return;
        }

        foreach (var tag in _tags)
        {
            var row = new Grid { Padding = new Thickness(0, 6, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var swatch = new Ellipse
            {
                Width = 16, Height = 16,
                Fill = ParseBrush(tag.Color),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(swatch, 0);

            var nameText = new TextBlock
            {
                Text = tag.Name,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };
            Grid.SetColumn(nameText, 1);

            var deleteBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE74D", FontSize = 12 },
                Padding = new Thickness(6, 4, 6, 4),
                Tag = tag.Id,
            };
            ToolTipService.SetToolTip(deleteBtn, "Delete tag");
            deleteBtn.Click += async (s, _) =>
            {
                if (s is Button btn && btn.Tag is int tagId)
                {
                    using var scope = App.Services.CreateScope();
                    var svc = scope.ServiceProvider.GetRequiredService<ITagService>();
                    await svc.DeleteAsync(tagId);
                    _tags.RemoveAll(t => t.Id == tagId);
                    RefreshList();
                    StatusText.Text = "Tag deleted.";
                }
            };
            Grid.SetColumn(deleteBtn, 2);

            row.Children.Add(swatch);
            row.Children.Add(nameText);
            row.Children.Add(deleteBtn);
            TagsList.Items.Add(row);
        }
    }

    private async void AddTagButton_Click(object sender, RoutedEventArgs e)
    {
        var name = TagNameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            StatusText.Text = "Please enter a tag name.";
            return;
        }

        if (_tags.Any(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            StatusText.Text = "A tag with that name already exists.";
            return;
        }

        var color = TagColorPicker.Color;
        var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        using var scope = App.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ITagService>();
        var newTag = await svc.CreateAsync(name, hex);

        _tags.Add(newTag);
        TagNameBox.Text = string.Empty;
        RefreshList();
        StatusText.Text = $"Tag \"{name}\" added.";
    }

    private static SolidColorBrush ParseBrush(string? hex)
    {
        if (!string.IsNullOrEmpty(hex))
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6 &&
                byte.TryParse(hex[0..2], System.Globalization.NumberStyles.HexNumber, null, out var r) &&
                byte.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g) &&
                byte.TryParse(hex[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
            {
                return new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
            }
        }
        return new SolidColorBrush(Colors.Gray);
    }
}
