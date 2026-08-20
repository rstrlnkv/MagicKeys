// MagicKeys — настройка Apple Magic Keyboard для Windows.
// Copyright (C) 2026 r.strlnkv
// Свободная программа под GNU General Public License v3 или новее; см. LICENSE.

using System;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Win32;

namespace MagicKeys
{
    /// <summary>Оформление в духе Windows 11: акцент и светлая/тёмная тема берутся из системы.</summary>
    internal static class Theme
    {
        public static bool IsDark { get { return ReadIsDark(); } }

        private static bool ReadIsDark()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false))
                {
                    if (k == null) return true;
                    object v = k.GetValue("AppsUseLightTheme");
                    if (v is int) return ((int)v) == 0;
                }
            }
            catch { }
            return true;
        }

        /// <summary>
        /// Тёмная ли панель задач. Это отдельный ключ: Windows разрешает светлые окна
        /// при тёмной панели и наоборот, а значку в трее важна именно панель.
        /// </summary>
        public static bool SystemIsDark
        {
            get
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false))
                    {
                        if (k != null)
                        {
                            object v = k.GetValue("SystemUsesLightTheme");
                            if (v is int) return ((int)v) == 0;
                        }
                    }
                }
                catch { }
                return true;
            }
        }

        public static Color Accent()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM", false))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("AccentColor");
                        if (v is int)
                        {
                            uint abgr = unchecked((uint)(int)v);
                            byte r = (byte)(abgr & 0xFF);
                            byte g = (byte)((abgr >> 8) & 0xFF);
                            byte b = (byte)((abgr >> 16) & 0xFF);
                            return Color.FromRgb(r, g, b);
                        }
                    }
                }
            }
            catch { }
            return Color.FromRgb(0x00, 0x78, 0xD4);
        }

        private static string Hex(Color c)
        {
            return "#" + c.A.ToString("X2") + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

        private static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb(255,
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));
        }

        public static double Luminance(Color c)
        {
            return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
        }

        /// <summary>Сообщает, что оформление пересобрано под новую тему системы.</summary>
        public static event Action Changed;

        private static ResourceDictionary _current;
        private static bool _appliedDark;
        private static Color _appliedAccent;

        /// <summary>
        /// Пересобирает оформление, если тема или акцент системы изменились, и
        /// подменяет словарь ресурсов на месте. Всё, что ссылается на кисти через
        /// SetResourceReference, перекрашивается само; остальное перечитывает
        /// подписчик события.
        /// </summary>
        public static bool Reapply()
        {
            bool dark = IsDark;
            Color accent = Accent();
            if (_current != null && dark == _appliedDark && accent == _appliedAccent) return false;

            ResourceDictionary fresh = Build();
            var app = Application.Current;
            if (app == null) return false;

            if (_current != null) app.Resources.MergedDictionaries.Remove(_current);
            app.Resources.MergedDictionaries.Add(fresh);
            _current = fresh;

            Action h = Changed;
            if (h != null) h();
            return true;
        }

        /// <summary>Первая сборка при запуске — заодно запоминает, что применено.</summary>
        public static ResourceDictionary Initial()
        {
            _current = Build();
            return _current;
        }

        public static ResourceDictionary Build()
        {
            bool dark = IsDark;
            Color accent = Accent();
            Color white = Colors.White, black = Colors.Black;

            // в тёмной теме системный акцент часто слишком тёмный — подсветляем
            Color accentUi = dark ? Mix(accent, white, Luminance(accent) < 0.35 ? 0.35 : 0.15) : accent;
            Color accentHover = dark ? Mix(accentUi, white, 0.12) : Mix(accentUi, white, 0.12);
            Color accentPress = Mix(accentUi, black, 0.15);
            string accentText = Luminance(accentUi) > 0.6 ? "#FF000000" : "#FFFFFFFF";

            string xaml = Xaml
                .Replace("{BG}", dark ? "#FF1F1F1F" : "#FFF3F3F3")
                .Replace("{LAYER}", dark ? "#FF2B2B2B" : "#FFFFFFFF")
                .Replace("{LAYERALT}", dark ? "#FF333333" : "#FFFBFBFB")
                .Replace("{FLYOUT}", dark ? "#FF2C2C2C" : "#FFF9F9F9")
                .Replace("{STROKE}", dark ? "#18FFFFFF" : "#14000000")
                .Replace("{STROKESTRONG}", dark ? "#28FFFFFF" : "#24000000")
                .Replace("{TEXT}", dark ? "#FFFFFFFF" : "#FF1A1A1A")
                .Replace("{TEXTSEC}", dark ? "#B0FFFFFF" : "#9E000000")
                .Replace("{TEXTTER}", dark ? "#78FFFFFF" : "#72000000")
                .Replace("{HOVER}", dark ? "#14FFFFFF" : "#0A000000")
                .Replace("{PRESSED}", dark ? "#0AFFFFFF" : "#14000000")
                .Replace("{ACCENT}", Hex(accentUi))
                .Replace("{ACCENTHOVER}", Hex(accentHover))
                .Replace("{ACCENTPRESS}", Hex(accentPress))
                .Replace("{ACCENTTEXT}", accentText)
                .Replace("{SCREEN}", dark ? "#FF3A3A3A" : "#FFE8E8E8")
                .Replace("{SCREENOFF}", dark ? "#FF262626" : "#FFEDEDED");

            _appliedDark = dark;
            _appliedAccent = accent;
            return (ResourceDictionary)XamlReader.Parse(xaml);
        }

        private const string Xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>

  <SolidColorBrush x:Key='Bg' Color='{BG}'/>
  <SolidColorBrush x:Key='Layer' Color='{LAYER}'/>
  <SolidColorBrush x:Key='LayerAlt' Color='{LAYERALT}'/>
  <SolidColorBrush x:Key='Flyout' Color='{FLYOUT}'/>
  <SolidColorBrush x:Key='Stroke' Color='{STROKE}'/>
  <SolidColorBrush x:Key='StrokeStrong' Color='{STROKESTRONG}'/>
  <SolidColorBrush x:Key='Text' Color='{TEXT}'/>
  <SolidColorBrush x:Key='TextSec' Color='{TEXTSEC}'/>
  <SolidColorBrush x:Key='TextTer' Color='{TEXTTER}'/>
  <SolidColorBrush x:Key='Hover' Color='{HOVER}'/>
  <SolidColorBrush x:Key='Pressed' Color='{PRESSED}'/>
  <SolidColorBrush x:Key='Accent' Color='{ACCENT}'/>
  <SolidColorBrush x:Key='AccentHover' Color='{ACCENTHOVER}'/>
  <SolidColorBrush x:Key='AccentPress' Color='{ACCENTPRESS}'/>
  <SolidColorBrush x:Key='AccentText' Color='{ACCENTTEXT}'/>
  <SolidColorBrush x:Key='Screen' Color='{SCREEN}'/>
  <SolidColorBrush x:Key='ScreenOff' Color='{SCREENOFF}'/>

  <FontFamily x:Key='UiFont'>Segoe UI Variable Text, Segoe UI</FontFamily>
  <FontFamily x:Key='UiFontDisplay'>Segoe UI Variable Display, Segoe UI</FontFamily>
  <FontFamily x:Key='IconFont'>Segoe Fluent Icons, Segoe MDL2 Assets</FontFamily>

  <Style TargetType='TextBlock'>
    <Setter Property='FontFamily' Value='{StaticResource UiFont}'/>
    <Setter Property='FontSize' Value='14'/>
    <Setter Property='Foreground' Value='{StaticResource Text}'/>
    <Setter Property='TextOptions.TextFormattingMode' Value='Ideal'/>
  </Style>

  <Style x:Key='Caption' TargetType='TextBlock'>
    <Setter Property='FontFamily' Value='{StaticResource UiFont}'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='Foreground' Value='{StaticResource TextSec}'/>
  </Style>

  <Style x:Key='Subtitle' TargetType='TextBlock'>
    <Setter Property='FontFamily' Value='{StaticResource UiFontDisplay}'/>
    <Setter Property='FontSize' Value='20'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
    <Setter Property='Foreground' Value='{StaticResource Text}'/>
  </Style>

  <Style x:Key='SectionHeader' TargetType='TextBlock'>
    <Setter Property='FontFamily' Value='{StaticResource UiFont}'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
    <Setter Property='Foreground' Value='{StaticResource TextTer}'/>
  </Style>

  <Style x:Key='Card' TargetType='Border'>
    <Setter Property='Background' Value='{StaticResource Layer}'/>
    <Setter Property='BorderBrush' Value='{StaticResource Stroke}'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='CornerRadius' Value='8'/>
  </Style>

  <Style x:Key='Btn' TargetType='Button'>
    <Setter Property='FontFamily' Value='{StaticResource UiFont}'/>
    <Setter Property='FontSize' Value='14'/>
    <Setter Property='Foreground' Value='{StaticResource Text}'/>
    <Setter Property='Background' Value='{StaticResource LayerAlt}'/>
    <Setter Property='BorderBrush' Value='{StaticResource StrokeStrong}'/>
    <Setter Property='Padding' Value='14,0'/>
    <Setter Property='Height' Value='34'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='Bd' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                  BorderThickness='1' CornerRadius='4' Padding='{TemplateBinding Padding}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='Bd' Property='Opacity' Value='0.85'/>
            </Trigger>
            <Trigger Property='IsPressed' Value='True'>
              <Setter TargetName='Bd' Property='Opacity' Value='0.6'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter TargetName='Bd' Property='Opacity' Value='0.4'/>
              <Setter Property='Cursor' Value='Arrow'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='BtnAccent' TargetType='Button' BasedOn='{StaticResource Btn}'>
    <Setter Property='Background' Value='{StaticResource Accent}'/>
    <Setter Property='Foreground' Value='{StaticResource AccentText}'/>
    <Setter Property='BorderBrush' Value='{StaticResource Accent}'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
  </Style>

  <Style x:Key='BtnSubtle' TargetType='Button' BasedOn='{StaticResource Btn}'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='BorderBrush' Value='Transparent'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='Bd' Background='Transparent' CornerRadius='4' Padding='{TemplateBinding Padding}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{StaticResource Hover}'/>
            </Trigger>
            <Trigger Property='IsPressed' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{StaticResource Pressed}'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter TargetName='Bd' Property='Opacity' Value='0.4'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Input' TargetType='TextBox'>
    <Setter Property='FontFamily' Value='{StaticResource UiFont}'/>
    <Setter Property='FontSize' Value='14'/>
    <Setter Property='Foreground' Value='{StaticResource Text}'/>
    <Setter Property='CaretBrush' Value='{StaticResource Text}'/>
    <Setter Property='Background' Value='{StaticResource LayerAlt}'/>
    <Setter Property='BorderBrush' Value='{StaticResource StrokeStrong}'/>
    <Setter Property='Height' Value='34'/>
    <Setter Property='Padding' Value='10,0'/>
    <Setter Property='VerticalContentAlignment' Value='Center'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='TextBox'>
          <Border x:Name='Bd' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                  BorderThickness='1' CornerRadius='4' Padding='{TemplateBinding Padding}'>
            <ScrollViewer x:Name='PART_ContentHost' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsFocused' Value='True'>
              <Setter TargetName='Bd' Property='BorderBrush' Value='{StaticResource Accent}'/>
              <Setter TargetName='Bd' Property='BorderThickness' Value='1,1,1,2'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='ComboItem' TargetType='ComboBoxItem'>
    <Setter Property='FontFamily' Value='{StaticResource UiFont}'/>
    <Setter Property='FontSize' Value='14'/>
    <Setter Property='Foreground' Value='{StaticResource Text}'/>
    <Setter Property='Padding' Value='10,7'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ComboBoxItem'>
          <Border x:Name='Bd' Background='Transparent' CornerRadius='4' Padding='{TemplateBinding Padding}' Margin='0,1'>
            <ContentPresenter/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{StaticResource Hover}'/>
            </Trigger>
            <Trigger Property='IsSelected' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{StaticResource Hover}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Combo' TargetType='ComboBox'>
    <Setter Property='FontFamily' Value='{StaticResource UiFont}'/>
    <Setter Property='FontSize' Value='14'/>
    <Setter Property='Foreground' Value='{StaticResource Text}'/>
    <Setter Property='Background' Value='{StaticResource LayerAlt}'/>
    <Setter Property='BorderBrush' Value='{StaticResource StrokeStrong}'/>
    <Setter Property='Height' Value='34'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='ItemContainerStyle' Value='{StaticResource ComboItem}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ComboBox'>
          <Grid>
            <Border x:Name='Bd' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                    BorderThickness='1' CornerRadius='4'/>
            <ToggleButton Background='Transparent' BorderThickness='0' Focusable='False' ClickMode='Press'
                          IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'>
              <ToggleButton.Template>
                <ControlTemplate TargetType='ToggleButton'>
                  <Border Background='Transparent'/>
                </ControlTemplate>
              </ToggleButton.Template>
            </ToggleButton>
            <ContentPresenter Margin='11,0,32,0' VerticalAlignment='Center' HorizontalAlignment='Left'
                              Content='{TemplateBinding SelectionBoxItem}'
                              ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}'
                              IsHitTestVisible='False'/>
            <TextBlock Text='&#xE70D;' FontFamily='{StaticResource IconFont}' FontSize='10'
                       HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,11,0'
                       Foreground='{StaticResource TextSec}' IsHitTestVisible='False'/>
            <Popup x:Name='PART_Popup' AllowsTransparency='True' Placement='Bottom' Focusable='False'
                   IsOpen='{TemplateBinding IsDropDownOpen}' PopupAnimation='Fade'>
              <Border Background='{StaticResource Flyout}' BorderBrush='{StaticResource Stroke}' BorderThickness='1'
                      CornerRadius='8' Padding='4' Margin='0,4,0,0'
                      MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}'>
                <Border.Effect>
                  <DropShadowEffect BlurRadius='18' ShadowDepth='3' Opacity='0.28' Color='#000000'/>
                </Border.Effect>
                <ScrollViewer MaxHeight='300'>
                  <ItemsPresenter/>
                </ScrollViewer>
              </Border>
            </Popup>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='Bd' Property='Opacity' Value='0.8'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter TargetName='Bd' Property='Opacity' Value='0.4'/>
              <Setter Property='Foreground' Value='{StaticResource TextTer}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Switch' TargetType='CheckBox'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='CheckBox'>
          <StackPanel Orientation='Horizontal'>
            <Border x:Name='Track' Width='40' Height='20' CornerRadius='10' Background='Transparent'
                    BorderBrush='{StaticResource TextSec}' BorderThickness='1' VerticalAlignment='Center'>
              <Ellipse x:Name='Knob' Width='12' Height='12' Fill='{StaticResource Text}'
                       HorizontalAlignment='Left' Margin='4,0,0,0'/>
            </Border>
            <ContentPresenter Margin='10,0,0,0' VerticalAlignment='Center'/>
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='Track' Property='Background' Value='{StaticResource Accent}'/>
              <Setter TargetName='Track' Property='BorderBrush' Value='{StaticResource Accent}'/>
              <Setter TargetName='Knob' Property='Fill' Value='{StaticResource AccentText}'/>
              <Setter TargetName='Knob' Property='HorizontalAlignment' Value='Right'/>
              <Setter TargetName='Knob' Property='Margin' Value='0,0,4,0'/>
              <Setter TargetName='Knob' Property='Width' Value='14'/>
              <Setter TargetName='Knob' Property='Height' Value='14'/>
            </Trigger>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='Track' Property='Opacity' Value='0.85'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Opacity' Value='0.4'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='ProfileItem' TargetType='ListBoxItem'>
    <Setter Property='Padding' Value='0'/>
    <Setter Property='Margin' Value='0,0,0,4'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ListBoxItem'>
          <Border x:Name='Bd' Background='Transparent' CornerRadius='6' Padding='10,8'>
            <Grid>
              <Border x:Name='Bar' Width='3' Height='18' CornerRadius='2' HorizontalAlignment='Left'
                      Background='{StaticResource Accent}' Visibility='Collapsed' Margin='-6,0,0,0'/>
              <ContentPresenter/>
            </Grid>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{StaticResource Hover}'/>
            </Trigger>
            <Trigger Property='IsSelected' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{StaticResource Hover}'/>
              <Setter TargetName='Bar' Property='Visibility' Value='Visible'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ScrollBar'>
    <Setter Property='Width' Value='8'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ScrollBar'>
          <Grid Background='Transparent'>
            <Track x:Name='PART_Track' IsDirectionReversed='True'>
              <Track.Thumb>
                <Thumb>
                  <Thumb.Template>
                    <ControlTemplate TargetType='Thumb'>
                      <Border Background='{StaticResource TextTer}' CornerRadius='3' Width='6'/>
                    </ControlTemplate>
                  </Thumb.Template>
                </Thumb>
              </Track.Thumb>
              <Track.IncreaseRepeatButton>
                <RepeatButton Command='ScrollBar.PageDownCommand' Opacity='0'/>
              </Track.IncreaseRepeatButton>
              <Track.DecreaseRepeatButton>
                <RepeatButton Command='ScrollBar.PageUpCommand' Opacity='0'/>
              </Track.DecreaseRepeatButton>
            </Track>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ToolTip'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ToolTip'>
          <Border Background='{StaticResource Flyout}' BorderBrush='{StaticResource Stroke}' BorderThickness='1'
                  CornerRadius='6' Padding='10,6'>
            <ContentPresenter/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Setter Property='Foreground' Value='{StaticResource Text}'/>
  </Style>

  <Style x:Key='FlyoutMenuItem' TargetType='MenuItem'>
    <Setter Property='FontFamily' Value='{StaticResource UiFont}'/>
    <Setter Property='FontSize' Value='14'/>
    <Setter Property='Foreground' Value='{StaticResource Text}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='MenuItem'>
          <Border x:Name='Bd' Background='Transparent' CornerRadius='4' Padding='10,7' Margin='0,1'>
            <Grid>
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width='18'/>
                <ColumnDefinition Width='*'/>
                <ColumnDefinition Width='Auto'/>
              </Grid.ColumnDefinitions>
              <Ellipse x:Name='Mark' Width='7' Height='7' Fill='{StaticResource Accent}'
                       HorizontalAlignment='Left' VerticalAlignment='Center' Visibility='Collapsed'/>
              <ContentPresenter Grid.Column='1' ContentSource='Header' VerticalAlignment='Center'/>
              <TextBlock Grid.Column='2' Text='{TemplateBinding InputGestureText}' Margin='18,0,0,0'
                         Foreground='{StaticResource TextTer}' FontSize='12' VerticalAlignment='Center'/>
              <TextBlock x:Name='Chevron' Grid.Column='2' Text='&#xE76C;' FontFamily='{StaticResource IconFont}'
                         FontSize='10' Margin='18,0,0,0' Foreground='{StaticResource TextSec}'
                         VerticalAlignment='Center' Visibility='Collapsed'/>
              <Popup x:Name='PART_Popup' Placement='Right' HorizontalOffset='2' AllowsTransparency='True'
                     Focusable='False' PopupAnimation='Fade'
                     IsOpen='{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}'>
                <Border Background='{StaticResource Flyout}' BorderBrush='{StaticResource Stroke}'
                        BorderThickness='1' CornerRadius='8' Padding='4' MinWidth='200' Margin='0,0,8,8'>
                  <Border.Effect>
                    <DropShadowEffect BlurRadius='20' ShadowDepth='4' Opacity='0.3' Color='#000000'/>
                  </Border.Effect>
                  <StackPanel IsItemsHost='True'/>
                </Border>
              </Popup>
            </Grid>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='HasItems' Value='True'>
              <Setter TargetName='Chevron' Property='Visibility' Value='Visible'/>
            </Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='Mark' Property='Visibility' Value='Visible'/>
              <Setter Property='FontWeight' Value='SemiBold'/>
            </Trigger>
            <Trigger Property='IsHighlighted' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{StaticResource Hover}'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Foreground' Value='{StaticResource TextTer}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='MenuSeparator' TargetType='Separator'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Separator'>
          <Border Height='1' Background='{StaticResource Stroke}' Margin='10,5'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='Separator' BasedOn='{StaticResource MenuSeparator}'/>

  <Style x:Key='FlyoutMenu' TargetType='ContextMenu'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ContextMenu'>
          <Border x:Name='Root' Background='{StaticResource Flyout}' BorderBrush='{StaticResource Stroke}'
                  BorderThickness='1' CornerRadius='8' Padding='4' MinWidth='220' Opacity='0'
                  RenderTransformOrigin='0.5,1'>
            <Border.RenderTransform>
              <TranslateTransform x:Name='Slide' Y='10'/>
            </Border.RenderTransform>
            <Border.Effect>
              <DropShadowEffect BlurRadius='20' ShadowDepth='4' Opacity='0.3' Color='#000000'/>
            </Border.Effect>
            <StackPanel IsItemsHost='True'/>
          </Border>
          <ControlTemplate.Triggers>
            <!-- своё появление: у меню с собственным шаблоном системная анимация не наследуется -->
            <Trigger Property='IsOpen' Value='True'>
              <Trigger.EnterActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName='Root' Storyboard.TargetProperty='Opacity'
                                     To='1' Duration='0:0:0.13'/>
                    <DoubleAnimation Storyboard.TargetName='Slide' Storyboard.TargetProperty='Y'
                                     To='0' Duration='0:0:0.18'>
                      <DoubleAnimation.EasingFunction>
                        <CubicEase EasingMode='EaseOut'/>
                      </DoubleAnimation.EasingFunction>
                    </DoubleAnimation>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.EnterActions>
              <Trigger.ExitActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName='Root' Storyboard.TargetProperty='Opacity'
                                     To='0' Duration='0:0:0.08'/>
                    <DoubleAnimation Storyboard.TargetName='Slide' Storyboard.TargetProperty='Y'
                                     To='10' Duration='0:0:0.08'/>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.ExitActions>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

</ResourceDictionary>";
    }
}
