// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using Microsoft.Win32;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace LumaDesk;

public partial class MainWindow
{
    internal UIElement CreateColorControls(DisplayInfo display, bool compact = false)
    {
        var section = new StackPanel();
        System.Windows.Automation.AutomationProperties.SetAutomationId(section, "color-controls-" + display.Number);
        var content = new StackPanel();
        var feedback = Ui.Text(L.T("sd69db33602"), 11, "#7A8699");
        bool busy = false;

        async Task Reload()
        {
            if (busy) return;
            busy = true; content.IsEnabled = false;
            try
            {
                var state = await Task.Run(() => AdvancedColorService.Read(display));
                RenderState(state); feedback.Text = "";
            }
            catch (Exception ex) { feedback.Text = ex.Message; }
            finally { busy = false; content.IsEnabled = true; }
        }
        async Task Change(Action action, string success)
        {
            if (busy) return;
            using var hold = (Window.GetWindow(section) as QuickWindow)?.KeepOpen();
            busy = true; content.IsEnabled = false; feedback.Text = L.T("s7c6fb681d0");
            try { await Task.Run(action); Status(success); feedback.Text = success; }
            catch (Exception ex) { feedback.Text = ex.Message; Status(ex.Message, true); }
            finally
            {
                try { RenderState(await Task.Run(() => AdvancedColorService.Read(display))); }
                catch (Exception ex) { feedback.Text += "\n" + ex.Message; }
                busy = false; content.IsEnabled = true;
            }
        }
        void RenderState(DisplayColorState state)
        {
            content.Children.Clear();
            var hdrStatus = state.HdrKnown ? (state.HdrActive ? L.T("s166778a827") : state.HdrUserEnabled ? L.T("sf5aebef902") : L.T("s3f68773e86")) : state.ColorMode;
            content.Children.Add(Ui.Row(Ui.Text("HDR", 12, "#354155", true), Ui.Text(hdrStatus, 11, "#7A8699")));
            var toggle = new CheckBox { Content = L.T("s13fbeb096f"), IsChecked = state.HdrUserEnabled, Margin = new Thickness(0, 8, 0, 5), IsEnabled = state.HdrKnown && state.HdrSupported && (!state.PolicyLimited || state.HdrUserEnabled) };
            System.Windows.Automation.AutomationProperties.SetAutomationId(toggle, "color-hdr-toggle-" + display.Number);
            toggle.Click += async (_, _) => { bool enabled = toggle.IsChecked == true; await Change(() => AdvancedColorService.SetHdr(display, enabled), enabled ? L.T("s79cef60880") : L.T("s7b979e66d2")); };
            content.Children.Add(toggle);
            content.Children.Add(Ui.Text(L.F("s9053c41639", state.ColorMode, state.Encoding, state.BitsPerChannel), 11, "#7A8699"));
            if (!string.IsNullOrEmpty(state.HdrUnavailableReason)) content.Children.Add(Ui.Text(state.HdrUnavailableReason, 11, "#8B7766"));
            if (state.SdrWhiteNits.HasValue) content.Children.Add(Ui.Text(L.F("s98674976b5", state.SdrWhiteNits), 11, "#7A8699"));
            content.Children.Add(Ui.Line());
            content.Children.Add(Ui.Text(L.T("s11e8e0fe46"), 12, "#354155", true));
            if (!state.ProfilesAvailable)
            {
                content.Children.Add(Ui.Text(state.ProfileUnavailableReason, 11, "#8B7766"));
                return;
            }
            content.Children.Add(Ui.Text(state.UseUserProfiles ? L.T("s1bfda6b24a") : L.T("s640f1df0a7"), 11, "#7A8699"));
            var kind = new ComboBox { ItemsSource = state.HdrSupported ? new[] { L.T("s934a4fdc56"), L.T("s98a58b2d5c") } : new[] { L.T("s934a4fdc56") }, SelectedIndex = state.HdrActive ? 1 : 0, Margin = new Thickness(0, 8, 0, 6), MinWidth = 130 };
            var profile = new ComboBox { ItemsSource = state.Profiles, Margin = new Thickness(0, 4, 0, 6), MinWidth = 160, MaxDropDownHeight = 220 };
            var current = Ui.Text("", 11, "#7A8699");
            void SelectKind()
            {
                string active = kind.SelectedIndex == 1 ? state.HdrProfile : state.SdrProfile;
                current.Text = L.T("s6606488056") + (string.IsNullOrEmpty(active) ? L.T("s57098ba8c4") : active);
                profile.SelectedItem = state.Profiles.FirstOrDefault(p => string.Equals(p, active, StringComparison.OrdinalIgnoreCase));
            }
            kind.SelectionChanged += (_, _) => SelectKind(); SelectKind();
            System.Windows.Automation.AutomationProperties.SetAutomationId(profile, "color-profile-list-" + display.Number);
            content.Children.Add(kind); content.Children.Add(current); content.Children.Add(profile);
            var apply = Ui.Button(L.T("sb4a0728bf1"), () =>
            {
                if (profile.SelectedItem is not string name) { feedback.Text = L.T("s41f0a5ba16"); return; }
                bool hdr = kind.SelectedIndex == 1;
                _ = Change(() => AdvancedColorService.SetDefaultProfile(display, name, hdr), L.T("s4e93f6c7cd") + (hdr ? " HDR" : " SDR") + L.T("s69bbb2916a"));
            });
            var import = Ui.Button(L.T("s4e771e80b0"), () =>
            {
                using var hold = (Window.GetWindow(section) as QuickWindow)?.KeepOpen();
                var chooser = new OpenFileDialog { Title = L.T("s4850365f1c"), Filter = L.T("se60e4c779d"), CheckFileExists = true, Multiselect = false };
                if (chooser.ShowDialog(Window.GetWindow(section)) != true) return;
                bool hdr = kind.SelectedIndex == 1;
                _ = Change(() => AdvancedColorService.InstallAndSetProfile(display, chooser.FileName, hdr,
                    message => Dispatcher.Invoke(() => feedback.Text = message)), L.T("s0e1ffa3345"));
            });
            if (compact) { content.Children.Add(apply); import.Margin = new Thickness(0, 5, 0, 0); content.Children.Add(import); }
            else content.Children.Add(Ui.Row(apply, import));
            if (state.UseUserProfiles)
            {
                var restore = Ui.Button(L.T("s227117f9db"), () => _ = Change(() => AdvancedColorService.UseSystemProfiles(display), L.T("s4c85212802")));
                restore.Margin = new Thickness(0, 7, 0, 0); content.Children.Add(restore);
            }
            if (!compact) content.Children.Add(Ui.Text(L.T("sbef663df9b"), 11, "#7A8699"));
        }
        section.Children.Add(Ui.Row(Ui.Text(compact ? L.T("sea5f32c81c") : L.T("s2b548851f5"), compact ? 11 : 13, "#354155", true), Ui.Button(L.T("saee8874341"), () => _ = Reload())));
        section.Children.Add(content); feedback.Margin = new Thickness(0, 7, 0, 0); section.Children.Add(feedback);
        section.Loaded += async (_, _) => await Reload();
        return section;
    }
}
