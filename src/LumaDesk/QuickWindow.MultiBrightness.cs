// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace LumaDesk;

internal sealed partial class QuickWindow
{
    private string? multiBrightnessError;

    private UIElement CreateMultiBrightnessControls()
    {
        // Freeze the identity set for this control's entire write queue. Selection
        // cannot change until the gesture and all queued writes have finished.
        var ids = Array.AsReadOnly(multiSelection.ToArray());
        var section = new StackPanel();
        var amount = Ui.Text("", 12, "#7C8594");
        AutomationProperties.SetAutomationId(amount, "quick-multi-amount");
        section.Children.Add(Ui.Row(Ui.Text(L.T("s8e2f643ad1"), 12), amount));
        var slider = new Slider { Minimum = 0, Maximum = 100, Height = 26 };
        var sliderHost = new Grid { Margin = new Thickness(0, 5, 0, 0) };
        var mixedTrack = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Ui.Brush("#B8B8B8"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1, 0, 1, 0), IsHitTestVisible = false };
        sliderHost.Children.Add(mixedTrack); sliderHost.Children.Add(slider);
        AutomationProperties.SetAutomationId(slider, "quick-multi-brightness");
        AutomationProperties.SetName(slider, L.T("s8e2f643ad1"));
        var help = Ui.Text(L.T("quick.multiHint"), 11, "#7A8699");
        help.TextWrapping = TextWrapping.Wrap; help.Margin = new Thickness(0, 6, 0, 0);
        var error = Ui.Text(multiBrightnessError ?? "", 11, "#B45136");
        error.TextWrapping = TextWrapping.Wrap; error.Margin = new Thickness(0, 7, 0, 0);
        error.Visibility = multiBrightnessError == null ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetAutomationId(error, "quick-multi-error");
        bool updating = false, dragging = false, mixed = false;
        IDisposable? lease = null;

        void Hold() => lease ??= KeepOpen();
        void Release() { lease?.Dispose(); lease = null; }
        void UpdateTrack()
        {
            mixedTrack.Visibility = mixed ? Visibility.Visible : Visibility.Collapsed;
            if (slider.Template?.FindName("PART_Track", slider) is not Track track) return;
            if (track.Thumb != null) track.Thumb.Opacity = mixed ? 0 : 1;
            if (track.DecreaseRepeatButton != null) track.DecreaseRepeatButton.Background = Ui.Brush(mixed ? "#B8B8B8" : "#007A8A");
        }
        void UpdateLevels()
        {
            foreach (var display in owner.Displays)
                if (multiLevelLabels.TryGetValue(display.Id, out var label)) label.Text = owner.GetCombinedBrightness(display) + "%";
        }
        void ReadActual()
        {
            var values = owner.Displays.Where(d => ids.Contains(d.Id, StringComparer.OrdinalIgnoreCase)).Select(owner.GetCombinedBrightness).ToArray();
            mixed = values.Distinct().Take(2).Count() > 1;
            updating = true;
            slider.IsEnabled = values.Length > 0;
            slider.Value = values.Length > 0 ? Math.Round(values.Average()) : 0;
            updating = false;
            amount.Text = values.Length == 0 ? L.T("quick.multiEmpty") : mixed ? L.T("quick.multiMixed") : values[0] + "%";
            AutomationProperties.SetItemStatus(slider, amount.Text);
            help.Text = values.Length == 0 ? L.T("quick.multiSelectHint") : L.T("quick.multiHint");
            UpdateLevels(); UpdateTrack();
        }
        void RefreshWhenIdle()
        {
            if (slider.IsLoaded && !dragging && !slider.IsMouseCaptureWithin && !slider.IsKeyboardFocusWithin) Render();
        }
        ReadActual();
        var writer = new LatestValueWriter(value => applySelectedBrightness(ids, value), (value, failure) =>
        {
            UpdateLevels();
            if (value != (int)Math.Round(slider.Value)) return;
            multiBrightnessError = failure?.Message;
            error.Text = multiBrightnessError ?? ""; error.Visibility = failure == null ? Visibility.Collapsed : Visibility.Visible;
            if (failure != null) owner.Status(failure.Message, true);
        }, idle: () =>
        {
            if (!dragging && !slider.IsMouseCaptureWithin) { ReadActual(); Release(); }
            RefreshWhenIdle();
        });
        void Request()
        {
            if (updating || !slider.IsEnabled) return;
            mixed = false; UpdateTrack();
            amount.Text = Math.Round(slider.Value) + "%"; AutomationProperties.SetItemStatus(slider, amount.Text);
            Hold(); writer.Request((int)Math.Round(slider.Value));
        }
        slider.Loaded += (_, _) => UpdateTrack();
        slider.ValueChanged += (_, _) => Request();
        slider.PreviewMouseLeftButtonDown += (_, _) =>
        {
            // A mixed state has no common thumb. Clicking exactly its seed value
            // must still apply the user's explicit choice even without ValueChanged.
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (slider.IsLoaded && mixed && !writer.IsActive) Request(); }));
        };
        slider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => { dragging = true; Hold(); }));
        slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(async (_, _) =>
        {
            dragging = false; await writer.WhenIdle;
            if (!dragging && !writer.IsActive) { ReadActual(); Release(); RefreshWhenIdle(); }
        }));
        slider.LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(new Action(() => { if (!writer.IsActive) RefreshWhenIdle(); }));
        slider.Unloaded += (_, _) => { dragging = false; writer.Stop(); Release(); };
        section.Children.Add(sliderHost); section.Children.Add(help); section.Children.Add(error);
        return section;
    }
}
