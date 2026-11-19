using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using NeuralTask.Core.Models;

namespace NeuralTask.App.Views;

public partial class KillTreeDialog : Window
{
    public bool IsConfirmed { get; private set; } = false;
    public bool TerminateEntireTree { get; private set; } = true;

    public KillTreeDialog(ProcessSnapshot rootProcess, IReadOnlyList<ProcessSnapshot> descendants)
    {
        InitializeComponent();

        // Enable dragging the window
        MouseDown += (s, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        };

        // Keyboard navigation
        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                IsConfirmed = false;
                Close();
            }
        };

        TextWarningSummary.Text = $"Root process '{rootProcess.Name}' (PID {rootProcess.Pid}) has {descendants.Count} active child tasks. Terminating the root without terminating its children may leave orphaned processes running in the background.";
        ButtonTerminateTree.Content = $"Terminate entire tree ({descendants.Count + 1} processes)";

        var allNames = new List<string> { $"{rootProcess.Name} ({rootProcess.Pid})" };
        allNames.AddRange(descendants.Select(d => $"{d.Name} ({d.Pid})"));
        TextTargetList.Text = "Target processes: " + string.Join("  •  ", allNames);

        RenderNeuralGraph(rootProcess, descendants);
    }

    private void RenderNeuralGraph(ProcessSnapshot root, IReadOnlyList<ProcessSnapshot> children)
    {
        GraphContainer.Children.Clear();

        // 1. Root Node Representation
        var rootPanel = CreateNodeCard(root, isRoot: true);
        GraphContainer.Children.Add(rootPanel);

        if (children.Count == 0) return;

        // 2. Trunk connector line
        var trunk = new Rectangle
        {
            Width = 2,
            Height = 16,
            Fill = (Brush)FindResource("BrushBorderStrong"),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        GraphContainer.Children.Add(trunk);

        // 3. Children container with branching connectors
        var childrenWrap = new WrapPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        foreach (var child in children)
        {
            var childStack = new StackPanel
            {
                Margin = new Thickness(8, 0, 8, 8),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var branchLine = new Rectangle
            {
                Width = 1.5,
                Height = 10,
                Fill = (Brush)FindResource("BrushBorderSubtle"),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            childStack.Children.Add(branchLine);

            var childCard = CreateNodeCard(child, isRoot: false);
            childStack.Children.Add(childCard);

            childrenWrap.Children.Add(childStack);
        }

        GraphContainer.Children.Add(childrenWrap);
    }

    private Border CreateNodeCard(ProcessSnapshot process, bool isRoot)
    {
        var borderBrush = isRoot
            ? (Brush)FindResource("BrushAccentDanger")
            : (Brush)FindResource("BrushBorderStrong");

        var badgeBrush = isRoot
            ? (Brush)FindResource("BrushAccentDanger")
            : (Brush)FindResource("BrushAccentPrimary");

        var card = new Border
        {
            Background = (Brush)FindResource("BrushSurface"),
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 7, 12, 7),
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = new DropShadowEffect
            {
                BlurRadius = 8,
                ShadowDepth = 1,
                Direction = 270,
                Color = Colors.Black,
                Opacity = 0.08
            }
        };

        var content = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };

        // Badge indicator
        var badge = new Border
        {
            Background = badgeBrush,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(0, 0, 8, 0)
        };
        var badgeText = new TextBlock
        {
            Text = isRoot ? "Root parent" : "Child process",
            FontSize = 9.5,
            FontWeight = FontWeights.SemiBold,
            FontFamily = (FontFamily)FindResource("FontFamilyUI"),
            Foreground = (Brush)FindResource("BrushButtonTextOnAccent")
        };
        badge.Child = badgeText;
        content.Children.Add(badge);

        // Name
        var nameText = new TextBlock
        {
            Text = process.Name,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            FontFamily = (FontFamily)FindResource("FontFamilyUI"),
            Foreground = (Brush)FindResource("BrushTextPrimary"),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(nameText);

        // PID
        var pidText = new TextBlock
        {
            Text = $"PID {process.Pid}",
            FontSize = 11,
            FontFamily = (FontFamily)FindResource("FontFamilyNumeric"),
            Foreground = (Brush)FindResource("BrushTextTertiary"),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(pidText);

        // RAM
        var ramText = new TextBlock
        {
            Text = $"{process.RamBytes / (1024.0 * 1024):0.0} MB",
            FontSize = 11,
            FontFamily = (FontFamily)FindResource("FontFamilyNumeric"),
            Foreground = (Brush)FindResource("BrushTextSecondary"),
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(ramText);

        card.Child = content;
        return card;
    }

    private void OnTerminateTreeClick(object sender, RoutedEventArgs e)
    {
        IsConfirmed = true;
        TerminateEntireTree = true;
        Close();
    }

    private void OnTerminateRootOnlyClick(object sender, RoutedEventArgs e)
    {
        IsConfirmed = true;
        TerminateEntireTree = false;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        Close();
    }
}
