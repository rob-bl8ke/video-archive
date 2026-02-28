using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using VideoArchive.Helpers;
using VideoArchive.Models;
using VideoArchive.Services;

namespace VideoArchive.Views;

/// <summary>
/// Programmatic ContentDialog for managing the global tag list (CRUD + color).
/// </summary>
public static class TagManagerDialog
{
    public static async Task ShowAsync(XamlRoot xamlRoot)
    {
        using var scope = App.Services.CreateScope();
        var tagService = scope.ServiceProvider.GetRequiredService<ITagService>();

        var tags = new List<Tag>(await tagService.GetAllAsync());

        // Build UI
        var listView = new ListView
        {
            SelectionMode = ListViewSelectionMode.None,
            MaxHeight = 300,
        };

        var nameBox = new TextBox { PlaceholderText = "Tag name", Width = 160 };
        var colorPicker = new ColorPicker
        {
            IsAlphaEnabled = false,
            IsHexInputVisible = true,
            IsColorSpectrumVisible = false,
            IsMoreButtonVisible = true,
            Color = Windows.UI.Color.FromArgb(255, 100, 149, 237), // cornflower blue default
        };
        var addButton = new Button { Content = "Add Tag", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };

        var addPanel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        addPanel.Children.Add(new TextBlock { Text = "New Tag", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
        addPanel.Children.Add(nameBox);
        addPanel.Children.Add(colorPicker);
        addPanel.Children.Add(addButton);

        var root = new StackPanel { Spacing = 8, MinWidth = 360 };
        root.Children.Add(listView);
        root.Children.Add(addPanel);

        // Populate list
        void RefreshList()
        {
            listView.Items.Clear();
            foreach (var tag in tags)
            {
                var row = new Grid { Padding = new Thickness(0, 4, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // Color swatch
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
                deleteBtn.Click += async (s, _) =>
                {
                    if (s is Button btn && btn.Tag is int tagId)
                    {
                        await tagService.DeleteAsync(tagId);
                        tags.RemoveAll(t => t.Id == tagId);
                        RefreshList();
                    }
                };
                Grid.SetColumn(deleteBtn, 2);

                row.Children.Add(swatch);
                row.Children.Add(nameText);
                row.Children.Add(deleteBtn);
                listView.Items.Add(row);
            }

            if (tags.Count == 0)
            {
                listView.Items.Add(new TextBlock
                {
                    Text = "No tags yet.",
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    Margin = new Thickness(0, 8, 0, 8),
                });
            }
        }

        RefreshList();

        addButton.Click += async (_, _) =>
        {
            var name = nameBox.Text.Trim();
            if (string.IsNullOrEmpty(name)) return;

            // Check duplicate
            if (tags.Any(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return;

            var color = colorPicker.Color;
            var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            var newTag = await tagService.CreateAsync(name, hex);
            tags.Add(newTag);
            nameBox.Text = string.Empty;
            RefreshList();
        };

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "Manage Tags",
            Content = root,
            CloseButtonText = "Done",
        };

        await DialogHelper.ShowWithOverlayHiddenAsync(dialog);
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
