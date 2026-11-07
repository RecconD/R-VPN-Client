using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace RVpn
{
    internal sealed partial class Desktop
    {
        private readonly List<RoutingRule> ruleDraft = new List<RoutingRule>();

        private void MountRuleEditor()
        {
            var types = Get<ComboBox>("RuleType");
            var actions = Get<ComboBox>("RuleAction");
            types.ItemsSource = RoutingRule.Types;
            actions.ItemsSource = RoutingRule.Actions;
            types.SelectionChanged += (sender, args) => PaintRuleInput();
            types.SelectedIndex = actions.SelectedIndex = 0;
            Get<TextBox>("RuleSearch").TextChanged += (sender, args) => PaintRuleRows();
            Get<Button>("AddRuleFirst").Click += (sender, args) => AddRule(0);
            Get<Button>("AddRuleLast").Click += (sender, args) => AddRule(ruleDraft.Count);
            Get<Button>("CancelRules").Click += (sender, args) => ResetRuleDraft();
            Get<Button>("SaveRules").Click += async (sender, args) => await SaveRuleDraft();
            Get<Button>("RulesRawToggle").Click += (sender, args) => ToggleRawRules();
            Get<Button>("ApplyRules").Click += async (sender, args) => await Reconnect("Применяем правила…");
            ResetRuleDraft();
        }

        private void PaintRuleInput()
        {
            var type = (string)Get<ComboBox>("RuleType").SelectedItem;
            var input = Get<TextBox>("RuleValue");
            input.IsEnabled = type != "MATCH";
            if (!input.IsEnabled) input.Clear();
            string example;
            switch (type)
            {
                case "PROCESS-NAME": example = "steam.exe"; break;
                case "PROCESS-PATH": example = @"C:\Games\app.exe"; break;
                case "DOMAIN-SUFFIX": example = "ru"; break;
                case "GEOIP":
                case "SRC-GEOIP": example = "RU или LAN"; break;
                case "GEOSITE": example = "category-ru"; break;
                case "RULE-SET": example = "geoip-ru или geosite-category-ru"; break;
                case "IP-CIDR":
                case "SRC-IP-CIDR": example = "192.168.0.0/16"; break;
                case "IP-CIDR6": example = "fc00::/7"; break;
                case "NETWORK": example = "tcp, udp или icmp"; break;
                case "IN-TYPE": example = "tun"; break;
                case "IN-NAME": example = "tun-in"; break;
                case "DST-PORT":
                case "SRC-PORT": example = "443"; break;
                case "MATCH": example = "Весь трафик"; break;
                default: example = "example.com"; break;
            }
            AutomationProperties.SetHelpText(input, example);
        }

        private void ResetRuleDraft()
        {
            ruleDraft.Clear();
            ruleDraft.AddRange(local.routingRules);
            Get<TextBox>("RuleSearch").Clear();
            ShowRawRules(false);
            PaintRuleRows();
            Text("RulesNotice", "Правки действуют после сохранения и переподключения.");
        }

        private void AddRule(int index)
        {
            var input = Get<TextBox>("RuleValue");
            var rule = RoutingRule.Parse((string)Get<ComboBox>("RuleType").SelectedItem, input.Text,
                (string)Get<ComboBox>("RuleAction").SelectedItem);
            if (rule == null) { Text("RulesNotice", "Проверь содержимое выбранного типа правила."); return; }
            if (ruleDraft.Count >= RoutingRule.Limit) { Text("RulesNotice", "Можно сохранить до 100 правил."); return; }
            ruleDraft.Insert(index, rule);
            input.Clear();
            Get<TextBox>("RuleSearch").Clear();
            PaintRuleRows();
            Text("RulesNotice", "Правило добавлено в черновик.");
        }

        private void PaintRuleRows()
        {
            var rows = Get<StackPanel>("RuleRows");
            var query = Get<TextBox>("RuleSearch").Text.Trim();
            rows.Children.Clear();
            foreach (var rule in ruleDraft.Where(item => item.Line.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                var row = new DockPanel { Margin = new Thickness(0, 1, 0, 4) };
                AddRuleButton(row, "×", "Удалить правило", () => { ruleDraft.Remove(rule); PaintRuleRows(); });
                AddRuleButton(row, "↓", "Переместить ниже", () => MoveRule(rule, 1));
                AddRuleButton(row, "↑", "Переместить выше", () => MoveRule(rule, -1));
                var info = new StackPanel { Margin = new Thickness(0, 4, 2, 4) };
                info.Children.Add(new TextBlock { Text = rule.value.Length == 0 ? "Весь трафик" : rule.value,
                    TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeights.SemiBold, ToolTip = rule.Line });
                info.Children.Add(new TextBlock { Text = rule.type + " · " + rule.action, Style = (Style)Window.Resources["MutedText"],
                    FontSize = 10, Margin = new Thickness(0, 2, 0, 0) });
                row.Children.Add(info);
                rows.Children.Add(new Border { Child = row, Background = Token("Inset"), CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(9, 2, 4, 2), Margin = new Thickness(0, 0, 0, 5) });
            }
            var shared = ManagedDomains().Where(domain => domain.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                "DOMAIN-SUFFIX DIRECT".IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            if (shared.Length > 0)
            {
                rows.Children.Add(new TextBlock { Text = "Список администратора · DIRECT",
                    Style = (Style)Window.Resources["MutedText"], Margin = new Thickness(0, 12, 0, 5) });
                foreach (var domain in shared)
                    rows.Children.Add(new TextBlock { Text = domain, TextTrimming = TextTrimming.CharacterEllipsis });
            }
            if (rows.Children.Count == 0)
                rows.Children.Add(new TextBlock { Text = query.Length == 0 ? "Список правил пуст." : "Совпадений нет.",
                    Style = (Style)Window.Resources["MutedText"] });
        }

        private void AddRuleButton(DockPanel row, string title, string name, Action change)
        {
            var button = new Button { Content = title, Width = 28, Padding = new Thickness(2), ToolTip = name,
                Style = (Style)Window.Resources["ControlButton"] };
            AutomationProperties.SetName(button, name);
            button.Click += (sender, args) => change();
            DockPanel.SetDock(button, Dock.Right);
            row.Children.Add(button);
        }

        private void MoveRule(RoutingRule rule, int offset)
        {
            var index = ruleDraft.IndexOf(rule);
            if (index + offset < 0 || index + offset >= ruleDraft.Count) return;
            ruleDraft.RemoveAt(index);
            ruleDraft.Insert(index + offset, rule);
            PaintRuleRows();
        }

        private void ToggleRawRules()
        {
            if (Get<Border>("RulesRawPanel").IsVisible)
            {
                if (!ReadRawRules()) return;
                ShowRawRules(false);
                PaintRuleRows();
            }
            else
            {
                Get<TextBox>("RulesRaw").Text = String.Join(Environment.NewLine, ruleDraft.Select(rule => rule.Line));
                ShowRawRules(true);
            }
        }

        private void ShowRawRules(bool raw)
        {
            Get<Border>("RulesRawPanel").Visibility = raw ? Visibility.Visible : Visibility.Collapsed;
            Get<Grid>("RulesForm").Visibility = raw ? Visibility.Collapsed : Visibility.Visible;
            Get<Button>("RulesRawToggle").Content = raw ? "Список" : "Дополнительно";
        }

        private bool ReadRawRules()
        {
            var lines = Get<TextBox>("RulesRaw").Text.Split('\n');
            var parsed = new List<RoutingRule>();
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index].Trim();
                if (line.Length == 0) continue;
                var rule = RoutingRule.ParseLine(line);
                if (rule == null || parsed.Count >= RoutingRule.Limit)
                {
                    Text("RulesNotice", "Проверь правило в строке " + (index + 1) + ".");
                    return false;
                }
                parsed.Add(rule);
            }
            ruleDraft.Clear();
            ruleDraft.AddRange(parsed);
            return true;
        }

        private async Task SaveRuleDraft()
        {
            if (Get<Border>("RulesRawPanel").IsVisible && !ReadRawRules()) return;
            var page = Get<Grid>("RulesPage");
            var draft = ruleDraft.ToArray();
            page.IsEnabled = false;
            Text("RulesNotice", "Проверяем правила…");
            try
            {
                if (!await Task.Run(() => EngineSession.CheckRules(directory, draft)))
                {
                    Text("RulesNotice", "Ядро VPN не приняло список. Проверь выражения и содержимое правил.");
                    return;
                }
                if (closed) return;
                var previous = local.routingRules;
                local.routingRules = draft.Select((rule, index) => index < previous.Length && previous[index].Line == rule.Line
                    ? previous[index] : rule).ToArray();
                if (!TrySave())
                {
                    local.routingRules = previous;
                    Text("RulesNotice", "Не удалось сохранить список.");
                    return;
                }
                ShowRawRules(false);
                PaintRuleRows();
                PaintRoutingNote();
                Text("RulesNotice", "Правила сохранены. Они вступят в силу при подключении.");
            }
            finally { page.IsEnabled = true; }
        }
    }
}
