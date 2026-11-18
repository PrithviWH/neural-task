using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

        TextWarningSummary.Text = $"Root process '{rootProcess.Name}' (PID {rootProcess.Pid}) has {descendants.Count} active child tasks. Terminating the root without terminating its children may leave orphaned processes running in the background.";
        ButtonTerminateTree.Content = $"TERMINATE ENTIRE TREE ({descendants.Count + 1} PROCESSES)";

        var allNames = new List<string> { $"{rootProcess.Name} ({rootProcess.Pid})" };
        allNames.AddRange(descendants.Select(d => $"{d.Name} ({d.Pid})"));
        TextTargetList.Text = "Target Processes: " + string.Join("  •  ", allNames);

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
            Fill = (Brush)FindResource("BrushAccentPrimary"),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        GraphContainer.Children.Add(trunk);

        // 3. Children container with branching connectors
        var childrenGrid = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        var childrenWrap = new WrapPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        foreach (var child in children)
        {
            var childStack = new StackPanel
            {
                Margin = new Thickness(10, 0, 10, 10),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var branchLine = new Rectangle
            {
                Width = 1.5,
                Height = 12,
                Fill = (Brush)FindResource("BrushAccentSecondary"),
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
            ? (Brush)FindResource("BrushAccentPrimary")
            : (Brush)FindResource("BrushAccentSecondary");

        var glowColor = isRoot
            ? (Color)FindResource("ColorAccentPrimary")
            : (Color)FindResource("ColorAccentSecondary");

        var card = new Border
        {
            Background = (Brush)FindResource("BrushBackgroundBase"),
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 8, 14, 8),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        card.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = isRoot ? 10 : 6,
            ShadowDepth = 0,
            Color = glowColor,
            Opacity = isRoot ? 0.7 : 0.4
        };

        var content = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };

        // Badge indicator
        var badge = new Border
        {
            Background = borderBrush,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 1, 4, 1),
            Margin = new Thickness(0, 0, 8, 0)
        };
        var badgeText = new TextBlock
        {
            Text = isRoot ? "PARENT ROOT" : "CHILD NODE",
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            FontFamily = (FontFamily)FindResource("FontFamilyNumeric"),
            Foreground = new SolidColorBrush(Color.FromRgb(10, 14, 20))
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
            Margin = new Thickness(0, 0, 8, 0)
        };
        content.Children.Add(nameText);

        // PID
        var pidText = new TextBlock
        {
            Text = $"PID: {process.Pid}",
            FontSize = 11,
            FontFamily = (FontFamily)FindResource("FontFamilyNumeric"),
            Foreground = (Brush)FindResource("BrushTextSecondary"),
            Margin = new Thickness(0, 0, 8, 0)
        };
        content.Children.Add(pidText);

        // RAM
        var ramText = new TextBlock
        {
            Text = $"{process.RamBytes / (1024.0 * 1024):0.0} MB",
            FontSize = 11,
            FontFamily = (FontFamily)FindResource("FontFamilyNumeric"),
            Foreground = borderBrush
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
