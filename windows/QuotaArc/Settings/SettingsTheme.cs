using System.Windows;

using System.Windows.Controls;

using System.Windows.Controls.Primitives;

using System.Windows.Documents;

using System.Windows.Media;

using QuotaArc.Design;



namespace QuotaArc.SettingsUi;



internal static class SettingsTheme

{

    public static readonly Brush WindowBackground = Freeze(new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0A)));

    public static readonly Brush SectionBackground = Freeze(new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x12)));

    public static readonly Brush RowBackground = Freeze(new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18)));

    public static readonly Brush SectionBorder = Freeze(new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)));

    public static readonly Brush SegmentTrack = Freeze(new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C)));

    public static readonly Brush SegmentSelected = Freeze(new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28)));

    public static readonly Brush TextPrimary = Palette.TextPrimaryBrush;

    public static readonly Brush TextSecondary = Palette.TextSecondaryBrush;

    public static readonly Brush TextMuted = Freeze(new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)));

    public static readonly Brush Accent = Palette.AmpleBrush;

    public static readonly Brush Warning = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x95, 0x00)));

    public static readonly Brush Destructive = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x3F, 0x00)));



    public static ScrollViewer DarkScrollViewer(UIElement content) => new()

    {

        Content = content,

        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,

        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,

        Background = WindowBackground,

        Padding = new Thickness(0)

    };



    public static Border SectionCard(UIElement content, Thickness? margin = null) => new()

    {

        Background = SectionBackground,

        BorderBrush = SectionBorder,

        BorderThickness = new Thickness(1),

        CornerRadius = new CornerRadius(12),

        Padding = new Thickness(4),

        Margin = margin ?? new Thickness(0, 0, 0, 20),

        Child = content

    };



    public static TextBlock SectionTitle(string text) => new()

    {

        Text = text,

        FontSize = 11,

        FontWeight = FontWeights.SemiBold,

        Foreground = TextMuted,

        Margin = new Thickness(8, 0, 0, 8)

    };



    public static TextBlock Body(string text, Brush? foreground = null) => new()

    {

        Text = text,

        FontSize = 13,

        Foreground = foreground ?? TextPrimary,

        TextWrapping = TextWrapping.Wrap,

        Margin = new Thickness(0, 0, 0, 8)

    };



    public static TextBlock Caption(string text, Brush? foreground = null) => new()

    {

        Text = text,

        FontSize = 12,

        Foreground = foreground ?? TextMuted,

        TextWrapping = TextWrapping.Wrap,

        Margin = new Thickness(0, 2, 0, 0),

        LineHeight = 18

    };



    public static Button PrimaryButton(string label, Action click)

    {

        var btn = new Button

        {

            Content = label,

            Padding = new Thickness(16, 10, 16, 10),

            FontSize = 13,

            FontWeight = FontWeights.SemiBold,

            Foreground = Freeze(new SolidColorBrush(Color.FromRgb(0x00, 0x1A, 0x0F))),

            Background = Accent,

            BorderThickness = new Thickness(0),

            Cursor = System.Windows.Input.Cursors.Hand

        };

        btn.Click += (_, _) => click();

        StyleFlatButton(btn, 8);

        return btn;

    }



    public static Button SecondaryButton(string label, Action click)

    {

        var btn = new Button

        {

            Content = label,

            Padding = new Thickness(16, 8, 16, 8),

            FontSize = 13,

            Foreground = TextPrimary,

            Background = SegmentTrack,

            BorderBrush = SectionBorder,

            BorderThickness = new Thickness(1),

            Cursor = System.Windows.Input.Cursors.Hand

        };

        btn.Click += (_, _) => click();

        StyleFlatButton(btn, 8);

        return btn;

    }



    public static TextBlock ActionLinks(params (string label, Action click)[] actions)

    {

        var block = new TextBlock

        {

            FontSize = 12,

            Margin = new Thickness(0, 6, 0, 0)

        };



        for (var i = 0; i < actions.Length; i++)

        {

            if (i > 0)

            {

                block.Inlines.Add(new Run("  ·  ")

                {

                    Foreground = TextMuted

                });

            }



            var (label, click) = actions[i];

            var link = new Hyperlink(new Run(label))

            {

                Foreground = Accent,

                TextDecorations = null

            };

            link.Click += (_, _) => click();

            block.Inlines.Add(link);

        }



        return block;

    }



    public static ToggleButton MakeSwitch(bool isOn, Action<bool> onChange)

    {

        var track = Freeze(new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)));

        var trackOn = Freeze(new SolidColorBrush(Color.FromArgb(0x55, 0x00, 0xFF, 0x88)));



        var toggle = new ToggleButton

        {

            IsChecked = isOn,

            Width = 44,

            Height = 24,

            Background = isOn ? trackOn : track,

            BorderThickness = new Thickness(0),

            Cursor = System.Windows.Input.Cursors.Hand,

            VerticalAlignment = VerticalAlignment.Center

        };



        var template = new ControlTemplate(typeof(ToggleButton));

        var border = new FrameworkElementFactory(typeof(Border), "track");

        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));

        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });



        var thumb = new FrameworkElementFactory(typeof(Border), "thumb");

        thumb.SetValue(Border.WidthProperty, 18.0);

        thumb.SetValue(Border.HeightProperty, 18.0);

        thumb.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));

        thumb.SetValue(Border.BackgroundProperty, Brushes.White);

        thumb.SetValue(Border.MarginProperty, new Thickness(3));

        thumb.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Left);

        thumb.SetValue(Border.VerticalAlignmentProperty, VerticalAlignment.Center);

        border.AppendChild(thumb);



        template.VisualTree = border;

        toggle.Template = template;



        toggle.Checked += (_, _) =>

        {

            toggle.Background = trackOn;

            onChange(true);

        };

        toggle.Unchecked += (_, _) =>

        {

            toggle.Background = track;

            onChange(false);

        };



        return toggle;

    }



    public static UIElement SegmentedPicker(string label, string[] items, int selected, Action<int> onChange)

    {

        var panel = new StackPanel { Margin = new Thickness(12, 8, 12, 4) };

        panel.Children.Add(new TextBlock

        {

            Text = label,

            FontSize = 13,

            FontWeight = FontWeights.SemiBold,

            Foreground = TextPrimary,

            Margin = new Thickness(0, 0, 0, 8)

        });

        panel.Children.Add(MakeSegmented(items, selected, onChange));

        return panel;

    }



    public static Border MakeSegmented(string[] items, int selected, Action<int> onChange)

    {

        var grid = new Grid();

        var buttons = new ToggleButton[items.Length];



        for (var i = 0; i < items.Length; i++)

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });



        for (var i = 0; i < items.Length; i++)

        {

            var idx = i;

            var btn = new ToggleButton

            {

                Content = items[i],

                IsChecked = i == selected,

                Padding = new Thickness(10, 7, 10, 7),

                FontSize = 12,

                Foreground = i == selected ? TextPrimary : TextSecondary,

                Background = i == selected ? SegmentSelected : Brushes.Transparent,

                BorderThickness = new Thickness(0),

                HorizontalContentAlignment = HorizontalAlignment.Center,

                Cursor = System.Windows.Input.Cursors.Hand

            };

            StyleSegment(btn);

            btn.Click += (_, _) =>

            {

                if (btn.IsChecked != true) return;

                onChange(idx);

                for (var j = 0; j < buttons.Length; j++)

                {

                    buttons[j].IsChecked = j == idx;

                    buttons[j].Background = j == idx ? SegmentSelected : Brushes.Transparent;

                    buttons[j].Foreground = j == idx ? TextPrimary : TextSecondary;

                }

            };

            Grid.SetColumn(btn, i);

            grid.Children.Add(btn);

            buttons[i] = btn;

        }



        return new Border

        {

            Background = SegmentTrack,

            BorderBrush = SectionBorder,

            BorderThickness = new Thickness(1),

            CornerRadius = new CornerRadius(8),

            Child = grid,

            ClipToBounds = true

        };

    }



    public static CheckBox Toggle(string label, bool isChecked, Action<bool> onChange)

    {

        var box = new CheckBox

        {

            Content = label,

            IsChecked = isChecked,

            Foreground = TextPrimary,

            FontSize = 13,

            Margin = new Thickness(12, 8, 12, 4),

            VerticalContentAlignment = VerticalAlignment.Center

        };

        box.Checked += (_, _) => onChange(true);

        box.Unchecked += (_, _) => onChange(false);

        return box;

    }



    public static Border Divider() => new()

    {

        Height = 1,

        Background = SectionBorder,

        Margin = new Thickness(12, 8, 12, 8)

    };



    private static void StyleFlatButton(Button btn, double radius)

    {

        var factory = new FrameworkElementFactory(typeof(Border), "border");

        factory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));

        factory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));

        factory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));

        factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));

        factory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

        var content = new FrameworkElementFactory(typeof(ContentPresenter));

        content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);

        content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);

        factory.AppendChild(content);

        btn.Template = new ControlTemplate(typeof(Button)) { VisualTree = factory };

    }



    private static void StyleSegment(ToggleButton btn)

    {

        var factory = new FrameworkElementFactory(typeof(Border), "border");

        factory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));

        factory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

        var content = new FrameworkElementFactory(typeof(ContentPresenter));

        content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);

        content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);

        factory.AppendChild(content);

        btn.Template = new ControlTemplate(typeof(ToggleButton)) { VisualTree = factory };

    }



    private static Brush Freeze(SolidColorBrush brush)

    {

        brush.Freeze();

        return brush;

    }

}


