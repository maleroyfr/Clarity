"""Migration script: Replace FluentAvalonia controls with SukiUI equivalents in all AXAML files."""
import os

BASE = r"C:\Users\MathieuLEROY\.copilot\worktrees\Clarity\maleroyfr\acclimatisable-nancey\src\Clarity.Desktop\Views"

# Icon path data constants
ICON_HOME = "M10,20V14H14V20H19V12H22L12,3L2,12H5V20H10Z"
ICON_PEOPLE = "M16 11c1.66 0 2.99-1.34 2.99-3S17.66 5 16 5c-1.66 0-3 1.34-3 3s1.34 3 3 3zm-8 0c1.66 0 2.99-1.34 2.99-3S9.66 5 8 5C6.34 5 5 6.34 5 8s1.34 3 3 3zm0 2c-2.33 0-7 1.17-7 3.5V19h14v-2.5c0-2.33-4.67-3.5-7-3.5zm8 0c-.29 0-.62.02-.97.05 1.16.84 1.97 1.97 1.97 3.45V19h6v-2.5c0-2.33-4.67-3.5-7-3.5z"
ICON_GLOBE = "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 17.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z"
ICON_LINK = "M3.9 12c0-1.71 1.39-3.1 3.1-3.1h4V7H7c-2.76 0-5 2.24-5 5s2.24 5 5 5h4v-1.9H7c-1.71 0-3.1-1.39-3.1-3.1zM8 13h8v-2H8v2zm9-6h-4v1.9h4c1.71 0 3.1 1.39 3.1 3.1s-1.39 3.1-3.1 3.1h-4V17h4c2.76 0 5-2.24 5-5s-2.24-5-5-5z"
ICON_CAMERA = "M9 2L7.17 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2h-3.17L15 2H9zm3 15c-2.76 0-5-2.24-5-5s2.24-5 5-5 5 2.24 5 5-2.24 5-5 5z"
ICON_ALLAPPS = "M4 8h4V4H4v4zm6 12h4v-4h-4v4zm-6 0h4v-4H4v4zm0-6h4v-4H4v4zm6 0h4v-4h-4v4zm6-10v4h4V4h-4zm-6 4h4V4h-4v4zm6 6h4v-4h-4v4zm0 6h4v-4h-4v4z"
ICON_LIBRARY = "M4 6H2v14c0 1.1.9 2 2 2h14v-2H4V6zm16-4H8c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2zm-1 9H9V9h10v2zm-4 4H9v-2h6v2zm4-8H9V5h10v2z"
ICON_DOWNLOAD = "M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z"
ICON_ADD = "M19 13h-6v6h-2v-6H5v-2h6V5h2v6h6v2z"
ICON_EDIT = "M3 17.25V21h3.75L17.81 9.94l-3.75-3.75L3 17.25zM20.71 7.04c.39-.39.39-1.02 0-1.41l-2.34-2.34c-.39-.39-1.02-.39-1.41 0l-1.83 1.83 3.75 3.75 1.83-1.83z"
ICON_DELETE = "M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z"
ICON_SETTINGS = "M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58c.18-.14.23-.41.12-.61l-1.92-3.32c-.12-.22-.37-.29-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54c-.04-.24-.24-.41-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.07.62-.07.94s.02.64.07.94l-2.03 1.58c-.18.14-.23.41-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z"
ICON_MAIL = "M20 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2zm0 4l-8 5-8-5V6l8 5 8-5v2z"
ICON_DOCUMENT = "M14 2H6c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6zm2 16H8v-2h8v2zm0-4H8v-2h8v2zM13 9V3.5L18.5 9H13z"
ICON_SAVE = "M17 3H5c-1.11 0-2 .9-2 2v14c0 1.1.89 2 2 2h14c1.1 0 2-.9 2-2V7l-4-4zm-5 16c-1.66 0-3-1.34-3-3s1.34-3 3-3 3 1.34 3 3-1.34 3-3 3zm3-10H5V5h10v4z"
ICON_PLAY = "M8 5v14l11-7z"
ICON_SHIELD = "M12 1L3 5v6c0 5.55 3.84 10.74 9 12 5.16-1.26 9-6.45 9-12V5l-9-4zm0 10.99h7c-.53 4.12-3.28 7.79-7 8.94V12H5V6.3l7-3.11v8.8z"

files = {}

# ============================================================
# 1. HomeView.axaml
# ============================================================
files[os.path.join(BASE, "Shell", "HomeView.axaml")] = f"""<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:Clarity.Desktop.ViewModels.Shell"
             xmlns:suki="https://github.com/kikipoulet/SukiUI"
             x:Class="Clarity.Desktop.Views.Shell.HomeView"
             x:DataType="vm:HomeViewModel">

  <ScrollViewer Padding="32,24">
    <StackPanel Spacing="28" MaxWidth="960">

      <!-- Welcome Header -->
      <StackPanel Spacing="4">
        <StackPanel Orientation="Horizontal" Spacing="12">
          <PathIcon Data="{ICON_HOME}" Width="32" Height="32"
                     Foreground="{{DynamicResource SystemAccentColor}}"
                     VerticalAlignment="Center"/>
          <StackPanel>
            <TextBlock Text="{{Binding WelcomeMessage}}" Classes="PageTitle"/>
            <TextBlock Text="{{Binding SubMessage}}" Classes="PageSubtitle"/>
          </StackPanel>
        </StackPanel>
      </StackPanel>

      <!-- Loading -->
      <StackPanel IsVisible="{{Binding IsLoading}}" Spacing="8"
                  HorizontalAlignment="Center" Margin="0,20">
        <ProgressBar IsIndeterminate="True" Width="200"/>
        <TextBlock Text="Loading dashboard..." HorizontalAlignment="Center"
                   Foreground="{{DynamicResource SystemBaseMediumColor}}"/>
      </StackPanel>

      <!-- Error -->
      <suki:InfoBar IsOpen="{{Binding ErrorMessage, Converter={{x:Static StringConverters.IsNotNullOrEmpty}}}}"
                    Severity="Error" Title="Error" Message="{{Binding ErrorMessage}}" IsClosable="True"/>

      <!-- Stats Cards Row -->
      <StackPanel IsVisible="{{Binding !IsLoading}}" Spacing="16">
        <TextBlock Text="Overview" Classes="SectionTitle"/>
        <WrapPanel Orientation="Horizontal" HorizontalAlignment="Left">
          <Border Classes="StatCard" Margin="0,0,12,12">
            <StackPanel Spacing="4">
              <PathIcon Data="{ICON_PEOPLE}" Width="22" Height="22"/>
              <TextBlock Text="{{Binding TotalCustomers}}" Classes="StatValue"/>
              <TextBlock Text="Customers" Classes="StatLabel"/>
            </StackPanel>
          </Border>
          <Border Classes="StatCard" Margin="0,0,12,12">
            <StackPanel Spacing="4">
              <PathIcon Data="{ICON_GLOBE}" Width="22" Height="22"/>
              <TextBlock Text="{{Binding TotalEnvironments}}" Classes="StatValue"/>
              <TextBlock Text="Environments" Classes="StatLabel"/>
            </StackPanel>
          </Border>
          <Border Classes="StatCard" Margin="0,0,12,12">
            <StackPanel Spacing="4">
              <PathIcon Data="{ICON_CAMERA}" Width="22" Height="22"/>
              <TextBlock Text="{{Binding TotalSnapshots}}" Classes="StatValue"/>
              <TextBlock Text="Snapshots" Classes="StatLabel"/>
            </StackPanel>
          </Border>
          <Border Classes="StatCard" Margin="0,0,12,12">
            <StackPanel Spacing="4">
              <PathIcon Data="{ICON_ALLAPPS}" Width="22" Height="22"/>
              <TextBlock Text="{{Binding TotalInventoryObjects}}" Classes="StatValue"/>
              <TextBlock Text="Inventory Objects" Classes="StatLabel"/>
            </StackPanel>
          </Border>
        </WrapPanel>
      </StackPanel>

      <!-- Quick Actions -->
      <StackPanel Spacing="12" IsVisible="{{Binding !IsLoading}}">
        <TextBlock Text="Quick Actions" Classes="SectionTitle"/>
        <WrapPanel Orientation="Horizontal" HorizontalAlignment="Left">

          <Button Classes="IconButton" Margin="0,0,12,12"
                  Command="{{Binding QuickNavigateCommand}}" CommandParameter="Customers">
            <Border Classes="ActionCard" MinWidth="200">
              <StackPanel Spacing="6">
                <StackPanel Orientation="Horizontal" Spacing="8">
                  <PathIcon Data="{ICON_PEOPLE}" Width="16" Height="16"/>
                  <TextBlock Text="Manage Customers" Classes="CardTitle"/>
                </StackPanel>
                <TextBlock Text="Create and manage client organisations" Classes="CardSubtitle" TextWrapping="Wrap"/>
              </StackPanel>
            </Border>
          </Button>

          <Button Classes="IconButton" Margin="0,0,12,12"
                  Command="{{Binding QuickNavigateCommand}}" CommandParameter="Environments">
            <Border Classes="ActionCard" MinWidth="200">
              <StackPanel Spacing="6">
                <StackPanel Orientation="Horizontal" Spacing="8">
                  <PathIcon Data="{ICON_GLOBE}" Width="16" Height="16"/>
                  <TextBlock Text="Manage Environments" Classes="CardTitle"/>
                </StackPanel>
                <TextBlock Text="Configure tenant and AD connections" Classes="CardSubtitle" TextWrapping="Wrap"/>
              </StackPanel>
            </Border>
          </Button>

          <Button Classes="IconButton" Margin="0,0,12,12"
                  Command="{{Binding QuickNavigateCommand}}" CommandParameter="Snapshots">
            <Border Classes="ActionCard" MinWidth="200">
              <StackPanel Spacing="6">
                <StackPanel Orientation="Horizontal" Spacing="8">
                  <PathIcon Data="{ICON_CAMERA}" Width="16" Height="16"/>
                  <TextBlock Text="Run Discovery" Classes="CardTitle"/>
                </StackPanel>
                <TextBlock Text="Create and manage inventory snapshots" Classes="CardSubtitle" TextWrapping="Wrap"/>
              </StackPanel>
            </Border>
          </Button>

          <Button Classes="IconButton" Margin="0,0,12,12"
                  Command="{{Binding QuickNavigateCommand}}" CommandParameter="Inventory">
            <Border Classes="ActionCard" MinWidth="200">
              <StackPanel Spacing="6">
                <StackPanel Orientation="Horizontal" Spacing="8">
                  <PathIcon Data="{ICON_ALLAPPS}" Width="16" Height="16"/>
                  <TextBlock Text="Browse Inventory" Classes="CardTitle"/>
                </StackPanel>
                <TextBlock Text="Explore collected objects and properties" Classes="CardSubtitle" TextWrapping="Wrap"/>
              </StackPanel>
            </Border>
          </Button>

          <Button Classes="IconButton" Margin="0,0,12,12"
                  Command="{{Binding QuickNavigateCommand}}" CommandParameter="Comparisons">
            <Border Classes="ActionCard" MinWidth="200">
              <StackPanel Spacing="6">
                <StackPanel Orientation="Horizontal" Spacing="8">
                  <PathIcon Data="{ICON_LIBRARY}" Width="16" Height="16"/>
                  <TextBlock Text="Compare Snapshots" Classes="CardTitle"/>
                </StackPanel>
                <TextBlock Text="Side-by-side snapshot comparison" Classes="CardSubtitle" TextWrapping="Wrap"/>
              </StackPanel>
            </Border>
          </Button>

          <Button Classes="IconButton" Margin="0,0,12,12"
                  Command="{{Binding QuickNavigateCommand}}" CommandParameter="Exports">
            <Border Classes="ActionCard" MinWidth="200">
              <StackPanel Spacing="6">
                <StackPanel Orientation="Horizontal" Spacing="8">
                  <PathIcon Data="{ICON_DOWNLOAD}" Width="16" Height="16"/>
                  <TextBlock Text="Export Data" Classes="CardTitle"/>
                </StackPanel>
                <TextBlock Text="Export to CSV, XLSX, or JSON" Classes="CardSubtitle" TextWrapping="Wrap"/>
              </StackPanel>
            </Border>
          </Button>

        </WrapPanel>
      </StackPanel>

      <!-- Workload Coverage -->
      <StackPanel Spacing="8" IsVisible="{{Binding !IsLoading}}">
        <TextBlock Text="Supported Workloads" Classes="SectionTitle"/>
        <WrapPanel Orientation="Horizontal">
          <Border Classes="CustomerCard" Margin="0,0,8,8" Padding="12,8">
            <StackPanel Orientation="Horizontal" Spacing="6">
              <PathIcon Data="{ICON_SETTINGS}" Width="14" Height="14"/>
              <TextBlock Text="Entra ID" FontSize="12" FontWeight="SemiBold" VerticalAlignment="Center"/>
            </StackPanel>
          </Border>
          <Border Classes="CustomerCard" Margin="0,0,8,8" Padding="12,8">
            <StackPanel Orientation="Horizontal" Spacing="6">
              <PathIcon Data="{ICON_DOCUMENT}" Width="14" Height="14"/>
              <TextBlock Text="Intune" FontSize="12" FontWeight="SemiBold" VerticalAlignment="Center"/>
            </StackPanel>
          </Border>
          <Border Classes="CustomerCard" Margin="0,0,8,8" Padding="12,8">
            <StackPanel Orientation="Horizontal" Spacing="6">
              <PathIcon Data="{ICON_MAIL}" Width="14" Height="14"/>
              <TextBlock Text="Exchange Online" FontSize="12" FontWeight="SemiBold" VerticalAlignment="Center"/>
            </StackPanel>
          </Border>
          <Border Classes="CustomerCard" Margin="0,0,8,8" Padding="12,8">
            <StackPanel Orientation="Horizontal" Spacing="6">
              <PathIcon Data="{ICON_DOCUMENT}" Width="14" Height="14"/>
              <TextBlock Text="SharePoint Online" FontSize="12" FontWeight="SemiBold" VerticalAlignment="Center"/>
            </StackPanel>
          </Border>
          <Border Classes="CustomerCard" Margin="0,0,8,8" Padding="12,8">
            <StackPanel Orientation="Horizontal" Spacing="6">
              <PathIcon Data="{ICON_PEOPLE}" Width="14" Height="14"/>
              <TextBlock Text="Teams" FontSize="12" FontWeight="SemiBold" VerticalAlignment="Center"/>
            </StackPanel>
          </Border>
          <Border Classes="CustomerCard" Margin="0,0,8,8" Padding="12,8">
            <StackPanel Orientation="Horizontal" Spacing="6">
              <PathIcon Data="{ICON_GLOBE}" Width="14" Height="14"/>
              <TextBlock Text="Active Directory" FontSize="12" FontWeight="SemiBold" VerticalAlignment="Center"/>
            </StackPanel>
          </Border>
        </WrapPanel>
      </StackPanel>

    </StackPanel>
  </ScrollViewer>
</UserControl>
"""

# ============================================================
# 2. CustomersListView.axaml
# ============================================================
files[os.path.join(BASE, "Customers", "CustomersListView.axaml")] = f"""<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:suki="https://github.com/kikipoulet/SukiUI"
             xmlns:vm="clr-namespace:Clarity.Desktop.ViewModels.Customers"
             x:Class="Clarity.Desktop.Views.Customers.CustomersListView"
             x:DataType="vm:CustomersListViewModel">

  <Grid RowDefinitions="Auto,Auto,Auto,*">

    <!-- Error InfoBar -->
    <suki:InfoBar Grid.Row="0"
                IsOpen="{{Binding ErrorMessage, Converter={{x:Static StringConverters.IsNotNullOrEmpty}}}}"
                Severity="Error" Title="Error" Message="{{Binding ErrorMessage}}"
                IsClosable="True" Margin="24,8,24,0"/>

    <!-- Page Header -->
    <Border Grid.Row="1" Padding="24,20,24,16"
            BorderBrush="{{DynamicResource SystemBaseLowColor}}"
            BorderThickness="0,0,0,1">
      <Grid ColumnDefinitions="*,Auto">
        <StackPanel Grid.Column="0">
          <TextBlock Text="Customers" Classes="PageTitle"/>
          <TextBlock Text="Manage client organisations" Classes="PageSubtitle"/>
        </StackPanel>
        <Button Grid.Column="1" Classes="PrimaryButton"
                Command="{{Binding CreateNewCommand}}"
                VerticalAlignment="Center">
          <StackPanel Orientation="Horizontal" Spacing="6">
            <PathIcon Data="{ICON_ADD}"/>
            <TextBlock Text="New Customer" VerticalAlignment="Center"/>
          </StackPanel>
        </Button>
      </Grid>
    </Border>

    <!-- Toolbar: Search + Filters -->
    <Border Grid.Row="2" Padding="24,12">
      <StackPanel Orientation="Horizontal" Spacing="12">
        <TextBox Watermark="Search customers..."
                 Text="{{Binding SearchText}}"
                 Width="280"
                 Classes="SearchBox"/>
        <CheckBox Content="Show archived"
                  IsChecked="{{Binding ShowArchived}}"/>
      </StackPanel>
    </Border>

    <!-- Loading / Empty / List -->
    <Panel Grid.Row="3">

      <!-- Loading state -->
      <StackPanel IsVisible="{{Binding IsLoading}}"
                  HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="12">
        <ProgressBar IsIndeterminate="True" Width="200"/>
        <TextBlock Text="Loading customers\u2026" HorizontalAlignment="Center"
                   Foreground="{{DynamicResource SystemBaseMediumColor}}"/>
      </StackPanel>

      <!-- Empty state -->
      <StackPanel IsVisible="{{Binding IsEmpty}}"
                  HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="8">
        <PathIcon Data="{ICON_PEOPLE}" Width="48" Height="48"/>
        <TextBlock Text="No customers yet" Classes="EmptyStateTitle"/>
        <TextBlock Text="Create your first customer to get started." Classes="EmptyStateSubtitle"/>
        <Button Classes="PrimaryButton"
                Command="{{Binding CreateNewCommand}}"
                HorizontalAlignment="Center" Margin="0,8,0,0">
          <StackPanel Orientation="Horizontal" Spacing="6">
            <PathIcon Data="{ICON_ADD}"/>
            <TextBlock Text="New Customer" VerticalAlignment="Center"/>
          </StackPanel>
        </Button>
      </StackPanel>

      <!-- Customer list -->
      <ScrollViewer IsVisible="{{Binding HasCustomers}}" Padding="24,8">
        <ItemsControl ItemsSource="{{Binding FilteredCustomers}}">
          <ItemsControl.ItemTemplate>
            <DataTemplate x:DataType="vm:CustomerListItemVm">
              <Border Classes="CustomerCard" Margin="0,0,0,8">
                <Grid ColumnDefinitions="*,Auto">
                  <StackPanel Grid.Column="0" Spacing="2">
                    <StackPanel Orientation="Horizontal" Spacing="8">
                      <TextBlock Text="{{Binding Name}}" Classes="CardTitle"/>
                      <Border IsVisible="{{Binding IsArchived}}" Classes="BadgeArchived">
                        <TextBlock Text="Archived" Classes="BadgeText"/>
                      </Border>
                    </StackPanel>
                    <TextBlock Text="{{Binding Description}}" Classes="CardSubtitle"
                               IsVisible="{{Binding HasDescription}}"/>
                    <TextBlock Classes="CardMeta"
                               Text="{{Binding CreatedAtDisplay}}"/>
                  </StackPanel>

                  <!-- Actions -->
                  <StackPanel Grid.Column="1" Orientation="Horizontal"
                              Spacing="8" VerticalAlignment="Center">
                    <Button Classes="PrimaryButton"
                            Command="{{Binding ViewEnvironmentsCommand}}"
                            ToolTip.Tip="Environments">
                      <StackPanel Orientation="Horizontal" Spacing="6">
                        <PathIcon Data="{ICON_GLOBE}"/>
                        <TextBlock Text="Environments" VerticalAlignment="Center"/>
                      </StackPanel>
                    </Button>
                    <Button Classes="SecondaryButton"
                            Command="{{Binding EditCommand}}"
                            ToolTip.Tip="Edit">
                      <StackPanel Orientation="Horizontal" Spacing="6">
                        <PathIcon Data="{ICON_EDIT}"/>
                        <TextBlock Text="Edit" VerticalAlignment="Center"/>
                      </StackPanel>
                    </Button>
                    <Button Classes="DangerButton" Content="Archive"
                            IsVisible="{{Binding !IsArchived}}"
                            Command="{{Binding ArchiveCommand}}"/>
                    <Button Classes="SecondaryButton" Content="Restore"
                            IsVisible="{{Binding IsArchived}}"
                            Command="{{Binding RestoreCommand}}"/>
                    <Button Classes="DangerButton"
                            Command="{{Binding DeleteCommand}}"
                            ToolTip.Tip="Delete">
                      <PathIcon Data="{ICON_DELETE}"/>
                    </Button>
                  </StackPanel>
                </Grid>
              </Border>
            </DataTemplate>
          </ItemsControl.ItemTemplate>
        </ItemsControl>
      </ScrollViewer>
    </Panel>
  </Grid>
</UserControl>
"""

# ============================================================
# 3. CustomerFormView.axaml
# ============================================================
files[os.path.join(BASE, "Customers", "CustomerFormView.axaml")] = f"""<UserControl xmlns="https://github.com/avaloniaui"
                  xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                  xmlns:suki="https://github.com/kikipoulet/SukiUI"
                  xmlns:vm="clr-namespace:Clarity.Desktop.ViewModels.Customers"
                  x:Class="Clarity.Desktop.Views.Customers.CustomerFormView"
                  x:DataType="vm:CustomerFormViewModel">

  <StackPanel Spacing="16" MinWidth="400">

    <StackPanel Spacing="4">
      <TextBlock Text="Customer name *" Classes="FormLabel"/>
      <TextBox Text="{{Binding Name}}" Watermark="e.g. Contoso Ltd" Classes="FormInput"/>
    </StackPanel>

    <StackPanel Spacing="4">
      <TextBlock Text="Description" Classes="FormLabel"/>
      <TextBox Text="{{Binding Description}}"
               Watermark="Optional description"
               AcceptsReturn="True"
               Height="80"
               Classes="FormInput"/>
    </StackPanel>

    <!-- Error message -->
    <suki:InfoBar IsOpen="{{Binding ErrorMessage, Converter={{x:Static StringConverters.IsNotNullOrEmpty}}}}"
                Severity="Error" Title="Error" Message="{{Binding ErrorMessage}}" IsClosable="True"/>
  </StackPanel>
</UserControl>
"""

# ============================================================
# 4. EnvironmentsListView.axaml
# ============================================================
files[os.path.join(BASE, "Environments", "EnvironmentsListView.axaml")] = f"""<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:Clarity.Desktop.ViewModels.Environments"
             xmlns:suki="https://github.com/kikipoulet/SukiUI"
             x:Class="Clarity.Desktop.Views.Environments.EnvironmentsListView"
             x:DataType="vm:EnvironmentsListViewModel">

  <Grid RowDefinitions="Auto,Auto,*,Auto">

    <!-- Page Header -->
    <Border Grid.Row="0" Padding="24,20,24,16"
            BorderBrush="{{DynamicResource SystemBaseLowColor}}"
            BorderThickness="0,0,0,1">
      <Grid ColumnDefinitions="*,Auto">
        <StackPanel Grid.Column="0">
          <StackPanel Orientation="Horizontal" Spacing="8">
            <PathIcon Data="{ICON_GLOBE}" Width="22" Height="22" VerticalAlignment="Center"/>
            <TextBlock Text="Environments" Classes="PageTitle"/>
          </StackPanel>
          <TextBlock Text="Manage customer environments" Classes="PageSubtitle"/>
        </StackPanel>
        <Button Grid.Column="1" Classes="PrimaryButton"
                Command="{{Binding CreateNewCommand}}"
                IsEnabled="{{Binding HasSelectedCustomer}}"
                VerticalAlignment="Center">
          <StackPanel Orientation="Horizontal" Spacing="6">
            <PathIcon Data="{ICON_ADD}" Width="14" Height="14"/>
            <TextBlock Text="New Environment" VerticalAlignment="Center"/>
          </StackPanel>
        </Button>
      </Grid>
    </Border>

    <!-- Toolbar with customer picker -->
    <Border Grid.Row="1" Padding="24,12">
      <StackPanel Spacing="12">
        <StackPanel Orientation="Horizontal" Spacing="12">
          <StackPanel Spacing="4">
            <TextBlock Text="Customer" FontSize="12"
                       Foreground="{{DynamicResource SystemBaseMediumColor}}"/>
            <ComboBox ItemsSource="{{Binding Customers}}"
                      SelectedItem="{{Binding SelectedCustomer}}"
                      PlaceholderText="Select a customer\u2026"
                      Width="280">
              <ComboBox.ItemTemplate>
                <DataTemplate>
                  <TextBlock Text="{{Binding Name}}"/>
                </DataTemplate>
              </ComboBox.ItemTemplate>
            </ComboBox>
          </StackPanel>
          <StackPanel Spacing="4" IsVisible="{{Binding HasSelectedCustomer}}">
            <TextBlock Text="Search" FontSize="12"
                       Foreground="{{DynamicResource SystemBaseMediumColor}}"/>
            <TextBox Watermark="Search environments..."
                     Text="{{Binding SearchText}}"
                     Width="280"
                     Classes="SearchBox"/>
          </StackPanel>
          <StackPanel Spacing="4" VerticalAlignment="Bottom" IsVisible="{{Binding HasSelectedCustomer}}">
            <CheckBox Content="Show archived"
                      IsChecked="{{Binding ShowArchived}}"
                      Margin="0,0,0,4"/>
          </StackPanel>
        </StackPanel>
      </StackPanel>
    </Border>

    <!-- Loading / Empty / No Customer / List -->
    <Panel Grid.Row="2">

      <!-- No customer selected -->
      <StackPanel IsVisible="{{Binding !HasSelectedCustomer}}"
                  HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="8">
        <PathIcon Data="{ICON_GLOBE}" Width="48" Height="48" HorizontalAlignment="Center"/>
        <TextBlock Text="Select a customer" Classes="EmptyStateTitle"/>
        <TextBlock Text="Choose a customer above to view and manage their environments."
                   Classes="EmptyStateSubtitle" TextAlignment="Center"/>
      </StackPanel>

      <StackPanel IsVisible="{{Binding IsLoading}}"
                  HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="12">
        <ProgressBar IsIndeterminate="True" Width="200"/>
        <TextBlock Text="Loading environments\u2026" HorizontalAlignment="Center"
                   Foreground="{{DynamicResource SystemBaseMediumColor}}"/>
      </StackPanel>

      <StackPanel IsVisible="{{Binding IsEmpty}}"
                  HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="8">
        <PathIcon Data="{ICON_GLOBE}" Width="48" Height="48" HorizontalAlignment="Center"/>
        <TextBlock Text="No environments yet" Classes="EmptyStateTitle"/>
        <TextBlock Text="Create your first environment to get started." Classes="EmptyStateSubtitle"/>
        <Button Classes="PrimaryButton"
                Command="{{Binding CreateNewCommand}}"
                HorizontalAlignment="Center" Margin="0,8,0,0">
          <StackPanel Orientation="Horizontal" Spacing="6">
            <PathIcon Data="{ICON_ADD}" Width="14" Height="14"/>
            <TextBlock Text="New Environment" VerticalAlignment="Center"/>
          </StackPanel>
        </Button>
      </StackPanel>

      <ScrollViewer IsVisible="{{Binding HasEnvironments}}" Padding="24,8">
        <ItemsControl ItemsSource="{{Binding FilteredEnvironments}}">
          <ItemsControl.ItemTemplate>
            <DataTemplate x:DataType="vm:EnvironmentListItemVm">
              <Border Classes="CustomerCard" Margin="0,0,0,8">
                <Grid ColumnDefinitions="*,Auto">
                  <StackPanel Grid.Column="0" Spacing="2">
                    <StackPanel Orientation="Horizontal" Spacing="8">
                      <TextBlock Text="{{Binding Name}}" Classes="CardTitle"/>
                      <!-- Type badge -->
                      <Border Background="{{DynamicResource SystemBaseLowColor}}"
                              CornerRadius="4" Padding="6,2">
                        <TextBlock Text="{{Binding TypeDisplay}}" Classes="BadgeText"/>
                      </Border>
                      <!-- Status badge -->
                      <Border Background="{{DynamicResource SystemBaseLowColor}}"
                              CornerRadius="4" Padding="6,2">
                        <TextBlock Text="{{Binding StatusDisplay}}" Classes="BadgeText"/>
                      </Border>
                      <Border IsVisible="{{Binding IsArchived}}" Classes="BadgeArchived">
                        <TextBlock Text="Archived" Classes="BadgeText"/>
                      </Border>
                    </StackPanel>
                    <TextBlock Text="{{Binding TenantDomain}}" Classes="CardSubtitle"
                               IsVisible="{{Binding TenantDomain, Converter={{x:Static StringConverters.IsNotNullOrEmpty}}}}"/>
                    <TextBlock Classes="CardMeta"
                               Text="{{Binding WorkloadCount, StringFormat='{{}}{{0}} workload(s) configured'}}"/>
                  </StackPanel>

                  <StackPanel Grid.Column="1" Orientation="Horizontal"
                              Spacing="8" VerticalAlignment="Center">
                    <Button Classes="SecondaryButton"
                            Command="{{Binding EditCommand}}">
                      <StackPanel Orientation="Horizontal" Spacing="6">
                        <PathIcon Data="{ICON_EDIT}" Width="14" Height="14"/>
                        <TextBlock Text="Edit" VerticalAlignment="Center"/>
                      </StackPanel>
                    </Button>
                    <Button Classes="SecondaryButton"
                            Command="{{Binding ConfigureAuthCommand}}">
                      <StackPanel Orientation="Horizontal" Spacing="6">
                        <PathIcon Data="{ICON_SETTINGS}" Width="14" Height="14"/>
                        <TextBlock Text="Configure" VerticalAlignment="Center"/>
                      </StackPanel>
                    </Button>
                    <Button Classes="DangerButton"
                            IsVisible="{{Binding !IsArchived}}"
                            Command="{{Binding ArchiveCommand}}"
                            Content="Archive"/>
                    <Button Classes="DangerButton"
                            Command="{{Binding DeleteCommand}}">
                      <StackPanel Orientation="Horizontal" Spacing="6">
                        <PathIcon Data="{ICON_DELETE}" Width="14" Height="14"/>
                        <TextBlock Text="Delete" VerticalAlignment="Center"/>
                      </StackPanel>
                    </Button>
                  </StackPanel>
                </Grid>
              </Border>
            </DataTemplate>
          </ItemsControl.ItemTemplate>
        </ItemsControl>
      </ScrollViewer>
    </Panel>

    <!-- Error bar -->
    <suki:InfoBar Grid.Row="3"
                IsOpen="{{Binding ErrorMessage, Converter={{x:Static StringConverters.IsNotNullOrEmpty}}}}"
                Severity="Error" Title="Error" Message="{{Binding ErrorMessage}}" IsClosable="True"
                Margin="24,0,24,16"/>
  </Grid>
</UserControl>
"""

# ============================================================
# 5. AuthConfigView.axaml
# ============================================================
files[os.path.join(BASE, "Environments", "AuthConfigView.axaml")] = f"""<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:suki="https://github.com/kikipoulet/SukiUI"
             xmlns:vm="clr-namespace:Clarity.Desktop.ViewModels.Environments"
             x:Class="Clarity.Desktop.Views.Environments.AuthConfigView"
             x:DataType="vm:AuthConfigViewModel">

  <ScrollViewer Padding="32,24">
    <StackPanel Spacing="8" MaxWidth="800">
      <TextBlock Text="{{Binding Title}}" FontSize="22" FontWeight="SemiBold"/>
      <TextBlock Text="Configure authentication for each workload"
                 Foreground="{{DynamicResource TextFillColorSecondaryBrush}}" Margin="0,0,0,16"/>

      <ItemsControl ItemsSource="{{Binding Workloads}}" x:CompileBindings="False">
        <ItemsControl.ItemTemplate>
          <DataTemplate>
            <StackPanel Spacing="4" Margin="0,0,0,8">
              <suki:GroupBox Header="{{Binding WorkloadDisplayName}}">
                <StackPanel Spacing="12" Margin="12">
                  <!-- Status -->
                  <StackPanel Orientation="Horizontal" Spacing="8">
                    <TextBlock Text="{{Binding StatusText}}" FontSize="12"
                               Foreground="{{DynamicResource SystemBaseMediumColor}}"/>
                    <Border CornerRadius="4" Padding="6,2"
                            Background="{{DynamicResource SystemBaseLowColor}}">
                      <TextBlock Text="{{Binding StatusLabel}}" FontSize="11"/>
                    </Border>
                  </StackPanel>

                  <!-- Auth type selector -->
                  <Grid ColumnDefinitions="Auto,*">
                    <TextBlock Text="Authentication Type" VerticalAlignment="Center" Margin="0,0,16,0"/>
                    <ComboBox Grid.Column="1" SelectedIndex="{{Binding SelectedAuthTypeIndex}}" MinWidth="180">
                      <ComboBoxItem Content="Certificate"/>
                      <ComboBoxItem Content="Client Secret"/>
                      <ComboBoxItem Content="Windows Integrated"/>
                    </ComboBox>
                  </Grid>

                  <!-- App fields (not for WindowsIntegrated) -->
                  <Grid ColumnDefinitions="Auto,*" IsVisible="{{Binding ShowAppFields}}">
                    <TextBlock Text="Application (Client) ID" VerticalAlignment="Center" Margin="0,0,16,0"/>
                    <TextBox Grid.Column="1" Text="{{Binding ClientId}}" Watermark="00000000-0000-0000-0000-000000000000" Width="320"/>
                  </Grid>

                  <Grid ColumnDefinitions="Auto,*" IsVisible="{{Binding ShowAppFields}}">
                    <TextBlock Text="Directory (Tenant) ID" VerticalAlignment="Center" Margin="0,0,16,0"/>
                    <TextBox Grid.Column="1" Text="{{Binding TenantId}}" Watermark="00000000-0000-0000-0000-000000000000" Width="320"/>
                  </Grid>

                  <!-- Certificate thumbprint -->
                  <Grid ColumnDefinitions="Auto,*" IsVisible="{{Binding ShowCertificateField}}">
                    <TextBlock Text="Certificate Thumbprint" VerticalAlignment="Center" Margin="0,0,16,0"/>
                    <TextBox Grid.Column="1" Text="{{Binding CertificateThumbprint}}" Watermark="Thumbprint from certificate store" Width="320"/>
                  </Grid>

                  <!-- Secret reference -->
                  <Grid ColumnDefinitions="Auto,*" IsVisible="{{Binding ShowSecretField}}">
                    <TextBlock Text="Secret Reference" VerticalAlignment="Center" Margin="0,0,16,0"/>
                    <TextBox Grid.Column="1" Text="{{Binding SecretReference}}" Watermark="Key Vault URI or DPAPI reference" Width="320"/>
                  </Grid>

                  <!-- Save button -->
                  <StackPanel Orientation="Horizontal" Spacing="8" HorizontalAlignment="Right">
                    <Button Classes="PrimaryButton" Command="{{Binding SaveCommand}}"
                            IsEnabled="{{Binding !IsSaving}}">
                      <StackPanel Orientation="Horizontal" Spacing="6">
                        <PathIcon Data="{ICON_SAVE}" Width="14" Height="14"/>
                        <TextBlock Text="Save" VerticalAlignment="Center"/>
                      </StackPanel>
                    </Button>
                    <ProgressBar IsIndeterminate="True" IsVisible="{{Binding IsSaving}}" Width="20" Height="20"/>
                  </StackPanel>
                </StackPanel>
              </suki:GroupBox>

              <!-- Success message -->
              <suki:InfoBar IsOpen="{{Binding SuccessMessage, Converter={{x:Static StringConverters.IsNotNullOrEmpty}}}}"
                          Severity="Success" Title="Saved" Message="{{Binding SuccessMessage}}" IsClosable="True"
                          Margin="0,2,0,0"/>

              <!-- Error message -->
              <suki:InfoBar IsOpen="{{Binding ErrorMessage, Converter={{x:Static StringConverters.IsNotNullOrEmpty}}}}"
                          Severity="Error" Title="Error" Message="{{Binding ErrorMessage}}" IsClosable="True"
                          Margin="0,2,0,0"/>
            </StackPanel>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>

      <!-- Global error -->
      <suki:InfoBar IsOpen="{{Binding ErrorMessage, Converter={{x:Static StringConverters.IsNotNullOrEmpty}}}}"
                  Severity="Error" Title="Error" Message="{{Binding ErrorMessage}}" IsClosable="True"
                  Margin="0,8,0,0"/>
    </StackPanel>
  </ScrollViewer>
</UserControl>
"""

# ============================================================
# 6. SnapshotsView.axaml
# ============================================================
files[os.path.join(BASE, "Snapshots", "SnapshotsView.axaml")] = f"""<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:suki="https://github.com/kikipoulet/SukiUI"
             xmlns:vm="clr-namespace:Clarity.Desktop.ViewModels.Snapshots"
             x:Class="Clarity.Desktop.Views.Snapshots.SnapshotsView"
             x:DataType="vm:SnapshotsViewModel">

  <Grid RowDefinitions="Auto,Auto,*,Auto">

    <!-- Page Header -->
    <Border Grid.Row="0" Padding="24,20,24,16"
            BorderBrush="{{DynamicResource SystemBaseLowColor}}"
            BorderThickness="0,0,0,1">
      <Grid ColumnDefinitions="*,Auto">
        <StackPanel Grid.Column="0" Orientation="Horizontal" Spacing="10">
          <PathIcon Data="{ICON_CAMERA}" Width="28" Height="28"/>
          <StackPanel>
            <TextBlock Text="Snapshots" Classes="PageTitle"/>
            <TextBlock Text="Create and manage environment snapshots" Classes="PageSubtitle"/>
          </StackPanel>
        </StackPanel>
        <Button Grid.Column="1" Classes="PrimaryButton"
                Command="{{Binding CreateSnapshotCommand}}"
                IsEnabled="{{Binding HasSelectedEnvironment}}"
                VerticalAlignment="Center">
          <Panel>
            <TextBlock Text="Creating\u2026" IsVisible="{{Binding IsCreating}}"/>
            <StackPanel Orientation="Horizontal" Spacing="6" IsVisible="{{Binding !IsCreating}}">
              <PathIcon Data="{ICON_ADD}" Width="16" Height="16"/>
              <TextBlock Text="New Snapshot"/>
            </StackPanel>
          </Panel>
        </Button>
      </Grid>
    </Border>

    <!-- Toolbar with customer + environment pickers -->
    <Border Grid.Row="1" Padding="24,12">
      <StackPanel Spacing="12">
        <StackPanel Orientation="Horizontal" Spacing="12">
          <StackPanel Spacing="4">
            <TextBlock Text="Customer" FontSize="12"
                       Foreground="{{DynamicResource SystemBaseMediumColor}}"/>
            <ComboBox ItemsSource="{{Binding Customers}}"
                      SelectedItem="{{Binding SelectedCustomer}}"
                      PlaceholderText="Select a customer\u2026"
                      Width="280">
              <ComboBox.ItemTemplate>
                <DataTemplate>
                  <TextBlock Text="{{Binding Name}}"/>
                </DataTemplate>
              </ComboBox.ItemTemplate>
            </ComboBox>
          </StackPanel>
          <StackPanel Spacing="4" IsVisible="{{Binding HasSelectedCustomer}}">
            <TextBlock Text="Environment" FontSize="12"
                       Foreground="{{DynamicResource SystemBaseMediumColor}}"/>
            <ComboBox ItemsSource="{{Binding Environments}}"
                      SelectedItem="{{Binding SelectedEnvironment}}"
                      PlaceholderText="Select an environment\u2026"
                      Width="280">
              <ComboBox.ItemTemplate>
                <DataTemplate>
                  <TextBlock Text="{{Binding Name}}"/>
                </DataTemplate>
              </ComboBox.ItemTemplate>
            </ComboBox>
          </StackPanel>
          <StackPanel Spacing="4" IsVisible="{{Binding HasSelectedEnvironment}}">
            <TextBlock Text="Search" FontSize="12"
                       Foreground="{{DynamicResource SystemBaseMediumColor}}"/>
            <TextBox Watermark="Search snapshots..."
                     Text="{{Binding SearchText}}"
                     Width="280"
                     Classes="SearchBox"/>
          </StackPanel>
        </StackPanel>
      </StackPanel>
    </Border>

    <!-- Loading / Empty / No Selection / List -->
    <Panel Grid.Row="2">

      <!-- No customer selected -->
      <StackPanel IsVisible="{{Binding !HasSelectedCustomer}}"
                  HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="8">
        <PathIcon Data="{ICON_PEOPLE}" Width="48" Height="48" HorizontalAlignment="Center"/>
        <TextBlock Text="Select a customer" Classes="EmptyStateTitle"/>
        <TextBlock Text="Choose a customer above to view their environments and snapshots."
                   Classes="EmptyStateSubtitle" TextAlignment="Center"/>
      </StackPanel>

      <!-- Customer selected but no environment -->
      <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="8">
        <StackPanel.IsVisible>
          <MultiBinding Converter="{{x:Static BoolConverters.And}}">
            <Binding Path="HasSelectedCustomer"/>
            <Binding Path="!HasSelectedEnvironment"/>
          </MultiBinding>
        </StackPanel.IsVisible>
        <PathIcon Data="{ICON_GLOBE}" Width="48" Height="48" HorizontalAlignment="Center"/>
        <TextBlock Text="Select an environment" Classes="EmptyStateTitle"/>
        <TextBlock Text="Choose an environment above to view its snapshots."
                   Classes="EmptyStateSubtitle" TextAlignment="Center"/>
      </StackPanel>

      <!-- Loading -->
      <StackPanel IsVisible="{{Binding IsLoading}}"
                  HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="12">
        <ProgressBar IsIndeterminate="True" Width="200"/>
        <TextBlock Text="Loading snapshots\u2026" HorizontalAlignment="Center"
                   Foreground="{{DynamicResource SystemBaseMediumColor}}"/>
      </StackPanel>

      <!-- Empty state -->
      <StackPanel IsVisible="{{Binding IsEmpty}}"
                  HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="8">
        <PathIcon Data="{ICON_CAMERA}" Width="48" Height="48" HorizontalAlignment="Center"/>
        <TextBlock Text="No snapshots yet" Classes="EmptyStateTitle"/>
        <TextBlock Text="Create your first snapshot to capture the environment state."
                   Classes="EmptyStateSubtitle"/>
        <Button Classes="PrimaryButton" HorizontalAlignment="Center" Margin="0,8,0,0"
                Command="{{Binding CreateSnapshotCommand}}">
          <StackPanel Orientation="Horizontal" Spacing="6">
            <PathIcon Data="{ICON_ADD}" Width="16" Height="16"/>
            <TextBlock Text="New Snapshot"/>
          </StackPanel>
        </Button>
      </StackPanel>

      <!-- Snapshot list -->
      <ScrollViewer IsVisible="{{Binding HasSnapshots}}" Padding="24,8">
        <ItemsControl ItemsSource="{{Binding FilteredSnapshots}}">
          <ItemsControl.ItemTemplate>
            <DataTemplate x:DataType="vm:SnapshotListItemVm">
              <Border Classes="CustomerCard" Margin="0,0,0,8">
                <Grid ColumnDefinitions="*,Auto">
                  <StackPanel Grid.Column="0" Spacing="2">
                    <StackPanel Orientation="Horizontal" Spacing="8">
                      <TextBlock Text="{{Binding Name}}" Classes="CardTitle"/>
                      <!-- Status badge -->
                      <Border Background="{{DynamicResource SystemBaseLowColor}}"
                              CornerRadius="4" Padding="6,2">
                        <TextBlock Text="{{Binding StatusDisplay}}" FontSize="12"
                                   VerticalAlignment="Center" Classes="BadgeText"/>
                      </Border>
                      <Border IsVisible="{{Binding IsImmutable}}" Classes="BadgeArchived">
                        <TextBlock Text="Sealed" Classes="BadgeText"/>
                      </Border>
                    </StackPanel>
                    <TextBlock Classes="CardMeta">
                      <TextBlock.Text>
                        <MultiBinding StringFormat="Created {{0:yyyy-MM-dd HH:mm}} \u00b7 {{1}} workload(s) \u00b7 {{2}} collector run(s)">
                          <Binding Path="CreatedAt"/>
                          <Binding Path="WorkloadScopeCount"/>
                          <Binding Path="CollectorRunCount"/>
                        </MultiBinding>
                      </TextBlock.Text>
                    </TextBlock>
                  </StackPanel>

                  <StackPanel Grid.Column="1" Orientation="Horizontal"
                              Spacing="8" VerticalAlignment="Center">
                    <Button Classes="SecondaryButton"
                            Command="{{Binding ViewInventoryCommand}}">
                      <StackPanel Orientation="Horizontal" Spacing="6">
                        <PathIcon Data="{ICON_LIBRARY}" Width="16" Height="16"/>
                        <TextBlock Text="View Inventory"/>
                      </StackPanel>
                    </Button>
                    <Button Classes="SecondaryButton"
                            Command="{{Binding ExportCommand}}">
                      <StackPanel Orientation="Horizontal" Spacing="6">
                        <PathIcon Data="{ICON_DOWNLOAD}" Width="16" Height="16"/>
                        <TextBlock Text="Export"/>
                      </StackPanel>
                    </Button>
                    <Button Classes="SuccessButton"
                            IsVisible="{{Binding CanSeal}}"
                            Command="{{Binding SealCommand}}">
                      <StackPanel Orientation="Horizontal" Spacing="6">
                        <PathIcon Data="{ICON_SHIELD}" Width="16" Height="16"/>
                        <TextBlock Text="Seal"/>
                      </StackPanel>
                    </Button>
                    <Button Classes="DangerButton"
                            Command="{{Binding DeleteCommand}}"
                            ToolTip.Tip="Delete snapshot">
                      <PathIcon Data="{ICON_DELETE}" Width="16" Height="16"/>
                    </Button>
                  </StackPanel>
                </Grid>
              </Border>
            </DataTemplate>
          </ItemsControl.ItemTemplate>
        </ItemsControl>
      </ScrollViewer>
    </Panel>

    <!-- Error bar -->
    <suki:InfoBar Grid.Row="3"
                IsOpen="{{Binding ErrorMessage, Converter={{x:Static StringConverters.IsNotNullOrEmpty}}}}"
                Severity="Error" Title="Error" Message="{{Binding ErrorMessage}}" IsClosable="True"
                Margin="24,0,24,16"/>
  </Grid>
</UserControl>
"""

# ============================================================
# 7. RelationsView.axaml
# ============================================================
files[os.path.join(BASE, "Relations", "RelationsView.axaml")] = f"""<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:Clarity.Desktop.ViewModels.Relations"
             xmlns:suki="https://github.com/kikipoulet/SukiUI"
             x:Class="Clarity.Desktop.Views.Relations.RelationsView"
             x:DataType="vm:RelationsViewModel">

  <Grid RowDefinitions="Auto,Auto,*,Auto">

    <!-- Page Header -->
    <Border Grid.Row="0" Padding="24,20,24,16"
            BorderBrush="{{DynamicResource SystemBaseLowColor}}"
            BorderThickness="0,0,0,1">
      <Grid ColumnDefinitions="*,Auto">
        <StackPanel Grid.Column="0">
          <StackPanel Orientation="Horizontal" Spacing="8">
            <PathIcon Data="{ICON_LINK}" Width="22" Height="22" VerticalAlignment="Center"/>
            <TextBlock Text="Environment Relations" Classes="PageTitle"/>
          </StackPanel>
          <TextBlock Text="Manage relationships between environments" Classes="PageSubtitle"/>
        </StackPanel>
        <Button Grid.Column="1" Classes="PrimaryButton"
                Command="{{Binding ShowFormCommand}}"
                IsEnabled="{{Binding HasSelectedCustomer}}"
                VerticalAlignment="Center">
          <StackPanel Orientation="Horizontal" Spacing="6">
            <PathIcon Data="{ICON_ADD}" Width="14" Height="14"/>
            <TextBlock Text="Add Relation" VerticalAlignment="Center"/>
          </StackPanel>
        </Button>
      </Grid>
    </Border>

    <!-- Toolbar with customer picker -->
    <Border Grid.Row="1" Padding="24,12">
      <StackPanel Orientation="Horizontal" Spacing="12">
        <StackPanel Spacing="4">
          <TextBlock Text="Customer" FontSize="12"
                     Foreground="{{DynamicResource SystemBaseMediumColor}}"/>
          <ComboBox ItemsSource="{{Binding Customers}}"
                    SelectedItem="{{Binding SelectedCustomer}}"
                    PlaceholderText="Select a customer\u2026"
                    Width="280">
            <ComboBox.ItemTemplate>
              <DataTemplate x:CompileBindings="False">
                <TextBlock Text="{{Binding Name}}"/>
              </DataTemplate>
            </ComboBox.ItemTemplate>
          </ComboBox>
        </StackPanel>
      </StackPanel>
    </Border>

    <!-- Main content area -->
    <Panel Grid.Row="2">

      <!-- No customer selected -->
      <StackPanel IsVisible="{{Binding !HasSelectedCustomer}}"
                  HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="8">
        <PathIcon Data="{ICON_LINK}" Width="48" Height="48" HorizontalAlignment="Center"/>
        <TextBlock Text="Select a customer" Classes="EmptyStateTitle"/>
        <TextBlock Text="Choose a customer above to view and manage environment relations."
                   Classes="EmptyStateSubtitle" TextAlignment="Center"/>
      </StackPanel>

      <!-- Loading state -->
      <StackPanel IsVisible="{{Binding IsLoading}}"
                  HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="12">
        <ProgressBar IsIndeterminate="True" Width="200"/>
        <TextBlock Text="Loading relations\u2026" HorizontalAlignment="Center"
                   Foreground="{{DynamicResource SystemBaseMediumColor}}"/>
      </StackPanel>

      <!-- Empty state (hide when form is open) -->
      <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="8">
        <StackPanel.IsVisible>
          <MultiBinding Converter="{{x:Static BoolConverters.And}}">
            <Binding Path="IsEmpty"/>
            <Binding Path="!IsFormVisible"/>
          </MultiBinding>
        </StackPanel.IsVisible>
        <PathIcon Data="{ICON_LINK}" Width="48" Height="48" HorizontalAlignment="Center"/>
        <TextBlock Text="No relations yet" Classes="EmptyStateTitle"/>
        <TextBlock Text="Create your first environment relation to get started."
                   Classes="EmptyStateSubtitle"/>
        <Button Classes="PrimaryButton"
                Command="{{Binding ShowFormCommand}}"
                HorizontalAlignment="Center" Margin="0,8,0,0">
          <StackPanel Orientation="Horizontal" Spacing="6">
            <PathIcon Data="{ICON_ADD}" Width="14" Height="14"/>
            <TextBlock Text="Add Relation" VerticalAlignment="Center"/>
          </StackPanel>
        </Button>
      </StackPanel>

      <!-- Relations list + inline form -->
      <ScrollViewer Padding="24,8">
        <ScrollViewer.IsVisible>
          <MultiBinding Converter="{{x:Static BoolConverters.Or}}">
            <Binding Path="HasRelations"/>
            <Binding Path="IsFormVisible"/>
          </MultiBinding>
        </ScrollViewer.IsVisible>
        <StackPanel Spacing="8">

          <!-- Inline create form -->
          <Border IsVisible="{{Binding IsFormVisible}}"
                  Classes="CustomerCard" Margin="0,0,0,8">
            <StackPanel Spacing="12">
              <TextBlock Text="New Relation" Classes="SectionTitle" Margin="0"/>

              <Grid ColumnDefinitions="*,*" RowDefinitions="Auto,Auto,Auto,Auto">
                <!-- Source environment -->
                <StackPanel Grid.Row="0" Grid.Column="0" Spacing="4" Margin="0,0,8,8">
                  <TextBlock Text="Source Environment *" Classes="FormLabel"/>
                  <ComboBox ItemsSource="{{Binding Environments}}"
                            SelectedItem="{{Binding SelectedSourceEnvironment}}"
                            PlaceholderText="Select source\u2026"
                            HorizontalAlignment="Stretch">
                    <ComboBox.ItemTemplate>
                      <DataTemplate x:CompileBindings="False">
                        <TextBlock Text="{{Binding Name}}"/>
                      </DataTemplate>
                    </ComboBox.ItemTemplate>
                  </ComboBox>
                </StackPanel>

                <!-- Target environment -->
                <StackPanel Grid.Row="0" Grid.Column="1" Spacing="4" Margin="8,0,0,8">
                  <TextBlock Text="Target Environment *" Classes="FormLabel"/>
                  <ComboBox ItemsSource="{{Binding Environments}}"
                            SelectedItem="{{Binding SelectedTargetEnvironment}}"
                            PlaceholderText="Select target\u2026"
                            HorizontalAlignment="Stretch">
                    <ComboBox.ItemTemplate>
                      <DataTemplate x:CompileBindings="False">
                        <TextBlock Text="{{Binding Name}}"/>
                      </DataTemplate>
                    </ComboBox.ItemTemplate>
                  </ComboBox>
                </StackPanel>

                <!-- Relation type -->
                <StackPanel Grid.Row="1" Grid.Column="0" Spacing="4" Margin="0,0,8,8">
                  <TextBlock Text="Relation Type" Classes="FormLabel"/>
                  <ComboBox ItemsSource="{{Binding RelationTypes}}"
                            SelectedItem="{{Binding SelectedRelationType}}"
                            HorizontalAlignment="Stretch"/>
                </StackPanel>

                <!-- Direction -->
                <StackPanel Grid.Row="1" Grid.Column="1" Spacing="4" Margin="8,0,0,8">
                  <TextBlock Text="Direction" Classes="FormLabel"/>
                  <ComboBox ItemsSource="{{Binding RelationDirections}}"
                            SelectedItem="{{Binding SelectedDirection}}"
                            HorizontalAlignment="Stretch"/>
                </StackPanel>

                <!-- Notes -->
                <StackPanel Grid.Row="2" Grid.ColumnSpan="2" Spacing="4" Margin="0,0,0,8">
                  <TextBlock Text="Notes" Classes="FormLabel"/>
                  <TextBox Text="{{Binding NewNotes}}"
                           Watermark="Optional notes about this relation"
                           AcceptsReturn="True"
                           Height="60"
                           Classes="FormInput"/>
                </StackPanel>

                <!-- Form actions -->
                <StackPanel Grid.Row="3" Grid.ColumnSpan="2"
                            Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8">
                  <Button Classes="SecondaryButton" Content="Cancel"
                          Command="{{Binding CancelFormCommand}}"/>
                  <Button Classes="PrimaryButton"
                          IsEnabled="{{Binding !IsSaving}}"
                          Command="{{Binding SaveRelationCommand}}">
                    <Panel>
                      <TextBlock Text="Saving\u2026" IsVisible="{{Binding IsSaving}}"/>
                      <StackPanel Orientation="Horizontal" Spacing="6" IsVisible="{{Binding !IsSaving}}">
                        <PathIcon Data="{ICON_SAVE}" Width="14" Height="14"/>
                        <TextBlock Text="Save Relation" VerticalAlignment="Center"/>
                      </StackPanel>
                    </Panel>
                  </Button>
                </StackPanel>
              </Grid>
            </StackPanel>
          </Border>

          <!-- Relation cards -->
          <ItemsControl ItemsSource="{{Binding Relations}}">
            <ItemsControl.ItemTemplate>
              <DataTemplate x:CompileBindings="False">
                <Border Classes="CustomerCard" Margin="0,0,0,8">
                  <Grid ColumnDefinitions="*,Auto">
                    <StackPanel Grid.Column="0" Spacing="4">
                      <!-- Source \u2192 Target -->
                      <StackPanel Orientation="Horizontal" Spacing="8">
                        <TextBlock Text="{{Binding SourceEnvironmentName}}" Classes="CardTitle"/>
                        <TextBlock Text="{{Binding DirectionArrow}}" FontSize="16"
                                   VerticalAlignment="Center"
                                   Foreground="{{DynamicResource SystemBaseMediumColor}}"/>
                        <TextBlock Text="{{Binding TargetEnvironmentName}}" Classes="CardTitle"/>
                      </StackPanel>

                      <!-- Type badge + direction -->
                      <StackPanel Orientation="Horizontal" Spacing="8">
                        <Border Classes="BadgeInfo" CornerRadius="4" Padding="6,2">
                          <TextBlock Text="{{Binding RelationTypeDisplay}}" Classes="BadgeText"/>
                        </Border>
                        <Border Background="{{DynamicResource SystemBaseLowColor}}"
                                CornerRadius="4" Padding="6,2">
                          <TextBlock Text="{{Binding DirectionDisplay}}" Classes="BadgeText"/>
                        </Border>
                      </StackPanel>

                      <!-- Notes -->
                      <TextBlock Text="{{Binding Notes}}" Classes="CardSubtitle"
                                 IsVisible="{{Binding HasNotes}}"
                                 TextWrapping="Wrap"/>

                      <!-- Created date -->
                      <TextBlock Text="{{Binding CreatedAtDisplay}}" Classes="CardMeta"/>
                    </StackPanel>

                    <!-- Delete action -->
                    <StackPanel Grid.Column="1" VerticalAlignment="Center">
                      <Button Classes="DangerButton"
                              Command="{{Binding DeleteCommand}}">
                        <StackPanel Orientation="Horizontal" Spacing="6">
                          <PathIcon Data="{ICON_DELETE}" Width="14" Height="14"/>
                          <TextBlock Text="Delete" VerticalAlignment="Center"/>
                        </StackPanel>
                      </Button>
                    </StackPanel>
                  </Grid>
                </Border>
              </DataTemplate>
            </ItemsControl.ItemTemplate>
          </ItemsControl>
        </StackPanel>
      </ScrollViewer>
    </Panel>

    <!-- Error bar -->
    <suki:InfoBar Grid.Row="3"
                IsOpen="{{Binding ErrorMessage, Converter={{x:Static StringConverters.IsNotNullOrEmpty}}}}"
                Severity="Error" Title="Error" Message="{{Binding ErrorMessage}}" IsClosable="True"
                Margin="24,0,24,16"/>
  </Grid>
</UserControl>
"""

# ============================================================
# 8. ComparisonView.axaml
# ============================================================
files[os.path.join(BASE, "Comparisons", "ComparisonView.axaml")] = f"""<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:Clarity.Desktop.ViewModels.Comparisons"
             xmlns:suki="https://github.com/kikipoulet/SukiUI"
             x:Class="Clarity.Desktop.Views.Comparisons.ComparisonView"
             x:DataType="vm:ComparisonViewModel">

  <Grid RowDefinitions="Auto,Auto,Auto,*,Auto">

    <!-- Page Header -->
    <Border Grid.Row="0" Padding="24,20,24,16"
            BorderBrush="{{DynamicResource SystemBaseLowColor}}"
            BorderThickness="0,0,0,1">
      <StackPanel>
        <StackPanel Orientation="Horizontal" Spacing="8">
          <PathIcon Data="{ICON_LIBRARY}" Width="22" Height="22" VerticalAlignment="Center"/>
          <TextBlock Text="Comparisons" Classes="PageTitle"/>
        </StackPanel>
        <TextBlock Text="Compare snapshots side by side" Classes="PageSubtitle"/>
      </StackPanel>
    </Border>

    <!-- Toolbar: customer + snapshot pickers + mode + run button -->
    <Border Grid.Row="1" Padding="24,12">
      <StackPanel Spacing="12">
        <!-- Row 1: Customer -->
        <StackPanel Orientation="Horizontal" Spacing="12">
          <StackPanel Spacing="4">
            <TextBlock Text="Customer" FontSize="12"
                       Foreground="{{DynamicResource SystemBaseMediumColor}}"/>
            <ComboBox ItemsSource="{{Binding Customers}}"
                      SelectedItem="{{Binding SelectedCustomer}}"
                      PlaceholderText="Select a customer\u2026"
                      Width="280">
              <ComboBox.ItemTemplate>
                <DataTemplate>
                  <TextBlock Text="{{Binding Name}}"/>
                </DataTemplate>
              </ComboBox.ItemTemplate>
            </ComboBox>
          </StackPanel>

          <StackPanel Spacing="4" IsVisible="{{Binding HasSelectedCustomer}}">
            <TextBlock Text="Comparison Mode" FontSize="12"
                       Foreground="{{DynamicResource SystemBaseMediumColor}}"/>
            <ComboBox ItemsSource="{{Binding ComparisonModes}}"
                      SelectedItem="{{Binding SelectedMode}}"
                      Width="220">
              <ComboBox.ItemTemplate>
                <DataTemplate>
                  <TextBlock Text="{{Binding DisplayName}}"/>
                </DataTemplate>
              </ComboBox.ItemTemplate>
            </ComboBox>
          </StackPanel>
        </StackPanel>

        <!-- Row 2: Left / Right snapshot pickers + Run button -->
        <StackPanel Orientation="Horizontal" Spacing="12"
                    IsVisible="{{Binding HasSelectedCustomer}}">

          <!-- Loading snapshots indicator -->
          <StackPanel Orientation="Horizontal" Spacing="6"
                      VerticalAlignment="Bottom" Margin="0,0,0,4"
                      IsVisible="{{Binding IsLoadingSnapshots}}">
            <ProgressBar IsIndeterminate="True" Width="80" Height="4"/>
            <TextBlock Text="Loading snapshots\u2026" FontSize="12"
                       Foreground="{{DynamicResource SystemBaseMediumColor}}"/>
          </StackPanel>

          <StackPanel Spacing="4" IsVisible="{{Binding !IsLoadingSnapshots}}">
            <TextBlock Text="Left Snapshot" FontSize="12"
                       Foreground="{{DynamicResource SystemBaseMediumColor}}"/>
            <ComboBox ItemsSource="{{Binding Snapshots}}"
                      SelectedItem="{{Binding SelectedLeftSnapshot}}"
                      PlaceholderText="Select left snapshot\u2026"
                      Width="300">
              <ComboBox.ItemTemplate>
                <DataTemplate x:CompileBindings="False">
                  <StackPanel Orientation="Horizontal" Spacing="6">
                    <TextBlock Text="{{Binding Name}}"/>
                    <TextBlock Text="{{Binding EnvironmentName, StringFormat='({{0}})'}}"
                               Foreground="{{DynamicResource SystemBaseMediumColor}}" FontSize="12"/>
                  </StackPanel>
                </DataTemplate>
              </ComboBox.ItemTemplate>
            </ComboBox>
          </StackPanel>

          <StackPanel Spacing="4" IsVisible="{{Binding !IsLoadingSnapshots}}">
            <TextBlock Text="Right Snapshot" FontSize="12"
                       Foreground="{{DynamicResource SystemBaseMediumColor}}"/>
            <ComboBox ItemsSource="{{Binding Snapshots}}"
                      SelectedItem="{{Binding SelectedRightSnapshot}}"
                      PlaceholderText="Select right snapshot\u2026"
                      Width="300">
              <ComboBox.ItemTemplate>
                <DataTemplate x:CompileBindings="False">
                  <StackPanel Orientation="Horizontal" Spacing="6">
                    <TextBlock Text="{{Binding Name}}"/>
                    <TextBlock Text="{{Binding EnvironmentName, StringFormat='({{0}})'}}"
                               Foreground="{{DynamicResource SystemBaseMediumColor}}" FontSize="12"/>
                  </StackPanel>
                </DataTemplate>
              </ComboBox.ItemTemplate>
            </ComboBox>
          </StackPanel>

          <StackPanel Spacing="4" VerticalAlignment="Bottom">
            <Button Classes="PrimaryButton"
                    Command="{{Binding RunComparisonCommand}}"
                    IsEnabled="{{Binding CanRunComparison}}">
              <StackPanel Orientation="Horizontal" Spacing="6">
                <PathIcon Data="{ICON_PLAY}" Width="14" Height="14"/>
                <TextBlock Text="Run Comparison" VerticalAlignment="Center"/>
              </StackPanel>
            </Button>
          </StackPanel>
        </StackPanel>
      </StackPanel>
    </Border>

    <!-- Running indicator -->
    <Border Grid.Row="2" Padding="24,8" IsVisible="{{Binding IsRunning}}">
      <StackPanel Orientation="Horizontal" Spacing="12" HorizontalAlignment="Center">
        <ProgressBar IsIndeterminate="True" Width="200"/>
        <TextBlock Text="Running comparison\u2026"
                   Foreground="{{DynamicResource SystemBaseMediumColor}}"
                   VerticalAlignment="Center"/>
      </StackPanel>
    </Border>

    <!-- Main content area: empty state or results -->
    <Panel Grid.Row="3">

      <!-- Empty state: no customer -->
      <StackPanel IsVisible="{{Binding !HasSelectedCustomer}}"
                  HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="8">
        <PathIcon Data="{ICON_LIBRARY}" Width="48" Height="48" HorizontalAlignment="Center"/>
        <TextBlock Text="Select a customer" Classes="EmptyStateTitle"/>
        <TextBlock Text="Choose a customer above to load snapshots for comparison."
                   Classes="EmptyStateSubtitle" TextAlignment="Center"/>
      </StackPanel>

      <!-- Empty state: no comparison run yet -->
      <StackPanel IsVisible="{{Binding !HasResults}}"
                  HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="8">
        <StackPanel.IsVisible>
          <MultiBinding Converter="{{x:Static BoolConverters.And}}">
            <Binding Path="HasSelectedCustomer"/>
            <Binding Path="!HasResults"/>
            <Binding Path="!IsRunning"/>
          </MultiBinding>
        </StackPanel.IsVisible>
        <PathIcon Data="{ICON_LIBRARY}" Width="48" Height="48" HorizontalAlignment="Center"/>
        <TextBlock Text="No comparison results yet" Classes="EmptyStateTitle"/>
        <TextBlock Text="Select two snapshots and run a comparison to see results."
                   Classes="EmptyStateSubtitle" TextAlignment="Center"/>
      </StackPanel>

      <!-- Results panel -->
      <ScrollViewer IsVisible="{{Binding HasResults}}" Padding="24,12">
        <StackPanel Spacing="16">

          <!-- Summary stat cards -->
          <TextBlock Text="Summary" Classes="CardTitle" Margin="0,0,0,4"/>
          <WrapPanel Orientation="Horizontal" HorizontalAlignment="Left">

            <!-- Total Left -->
            <Border Background="#F0F4FF" CornerRadius="8" Padding="16,12" Margin="0,0,12,8" MinWidth="130">
              <StackPanel Spacing="2">
                <TextBlock Text="Total Left" FontSize="12"
                           Foreground="{{DynamicResource SystemBaseMediumColor}}"/>
                <TextBlock Text="{{Binding TotalLeft}}" FontSize="24" FontWeight="Bold"
                           Foreground="#3366CC"/>
              </StackPanel>
            </Border>

            <!-- Total Right -->
            <Border Background="#F0F4FF" CornerRadius="8" Padding="16,12" Margin="0,0,12,8" MinWidth="130">
              <StackPanel Spacing="2">
                <TextBlock Text="Total Right" FontSize="12"
                           Foreground="{{DynamicResource SystemBaseMediumColor}}"/>
                <TextBlock Text="{{Binding TotalRight}}" FontSize="24" FontWeight="Bold"
                           Foreground="#3366CC"/>
              </StackPanel>
            </Border>

            <!-- Added -->
            <Border Background="#E8F5E9" CornerRadius="8" Padding="16,12" Margin="0,0,12,8" MinWidth="130">
              <StackPanel Spacing="2">
                <TextBlock Text="Added" FontSize="12" Foreground="#2E7D32"/>
                <TextBlock Text="{{Binding Added}}" FontSize="24" FontWeight="Bold"
                           Foreground="#2E7D32"/>
              </StackPanel>
            </Border>

            <!-- Removed -->
            <Border Background="#FFEBEE" CornerRadius="8" Padding="16,12" Margin="0,0,12,8" MinWidth="130">
              <StackPanel Spacing="2">
                <TextBlock Text="Removed" FontSize="12" Foreground="#C62828"/>
                <TextBlock Text="{{Binding Removed}}" FontSize="24" FontWeight="Bold"
                           Foreground="#C62828"/>
              </StackPanel>
            </Border>

            <!-- Modified -->
            <Border Background="#FFF3E0" CornerRadius="8" Padding="16,12" Margin="0,0,12,8" MinWidth="130">
              <StackPanel Spacing="2">
                <TextBlock Text="Modified" FontSize="12" Foreground="#E65100"/>
                <TextBlock Text="{{Binding Modified}}" FontSize="24" FontWeight="Bold"
                           Foreground="#E65100"/>
              </StackPanel>
            </Border>

            <!-- Unchanged -->
            <Border Background="#F5F5F5" CornerRadius="8" Padding="16,12" Margin="0,0,12,8" MinWidth="130">
              <StackPanel Spacing="2">
                <TextBlock Text="Unchanged" FontSize="12" Foreground="#757575"/>
                <TextBlock Text="{{Binding Unchanged}}" FontSize="24" FontWeight="Bold"
                           Foreground="#757575"/>
              </StackPanel>
            </Border>
          </WrapPanel>

          <!-- Per-workload breakdown -->
          <TextBlock Text="Per-Workload Breakdown" Classes="CardTitle" Margin="0,8,0,4"/>
          <Border Background="{{DynamicResource SystemBaseLowColor}}" CornerRadius="8" Padding="0">
            <Grid>
              <StackPanel>
                <!-- Header row -->
                <Border Padding="12,8" BorderThickness="0,0,0,1"
                        BorderBrush="{{DynamicResource SystemBaseMediumColor}}">
                  <Grid ColumnDefinitions="*,80,80,80,80">
                    <TextBlock Grid.Column="0" Text="Workload" FontWeight="SemiBold" FontSize="13"/>
                    <TextBlock Grid.Column="1" Text="Added" FontWeight="SemiBold" FontSize="13"
                               Foreground="#2E7D32" HorizontalAlignment="Right"/>
                    <TextBlock Grid.Column="2" Text="Removed" FontWeight="SemiBold" FontSize="13"
                               Foreground="#C62828" HorizontalAlignment="Right"/>
                    <TextBlock Grid.Column="3" Text="Modified" FontWeight="SemiBold" FontSize="13"
                               Foreground="#E65100" HorizontalAlignment="Right"/>
                    <TextBlock Grid.Column="4" Text="Unchanged" FontWeight="SemiBold" FontSize="13"
                               Foreground="#757575" HorizontalAlignment="Right"/>
                  </Grid>
                </Border>

                <!-- Data rows -->
                <ItemsControl ItemsSource="{{Binding WorkloadBreakdown}}">
                  <ItemsControl.ItemTemplate>
                    <DataTemplate x:CompileBindings="False">
                      <Border Padding="12,6" BorderThickness="0,0,0,1"
                              BorderBrush="{{DynamicResource SystemBaseLowColor}}">
                        <Grid ColumnDefinitions="*,80,80,80,80">
                          <TextBlock Grid.Column="0" Text="{{Binding WorkloadArea}}" FontSize="13"/>
                          <TextBlock Grid.Column="1" Text="{{Binding Added}}" FontSize="13"
                                     Foreground="#2E7D32" HorizontalAlignment="Right"/>
                          <TextBlock Grid.Column="2" Text="{{Binding Removed}}" FontSize="13"
                                     Foreground="#C62828" HorizontalAlignment="Right"/>
                          <TextBlock Grid.Column="3" Text="{{Binding Modified}}" FontSize="13"
                                     Foreground="#E65100" HorizontalAlignment="Right"/>
                          <TextBlock Grid.Column="4" Text="{{Binding Unchanged}}" FontSize="13"
                                     Foreground="#757575" HorizontalAlignment="Right"/>
                        </Grid>
                      </Border>
                    </DataTemplate>
                  </ItemsControl.ItemTemplate>
                </ItemsControl>
              </StackPanel>
            </Grid>
          </Border>

        </StackPanel>
      </ScrollViewer>
    </Panel>

    <!-- Error bar -->
    <suki:InfoBar Grid.Row="4"
                IsOpen="{{Binding ErrorMessage, Converter={{x:Static StringConverters.IsNotNullOrEmpty}}}}"
                Severity="Error" Title="Error" Message="{{Binding ErrorMessage}}" IsClosable="True"
                Margin="24,0,24,16"/>
  </Grid>
</UserControl>
"""

# ============================================================
# 9. ExportsView.axaml
# ============================================================
files[os.path.join(BASE, "Exports", "ExportsView.axaml")] = f"""<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:Clarity.Desktop.ViewModels.Exports"
             xmlns:enums="clr-namespace:Clarity.SharedContracts.Enums;assembly=Clarity.SharedContracts"
             xmlns:suki="https://github.com/kikipoulet/SukiUI"
             x:Class="Clarity.Desktop.Views.Exports.ExportsView"
             x:DataType="vm:ExportsViewModel">

  <Grid RowDefinitions="Auto,*,Auto">

    <!-- Page Header -->
    <Border Grid.Row="0" Padding="24,20,24,16"
            BorderBrush="{{DynamicResource SystemBaseLowColor}}"
            BorderThickness="0,0,0,1">
      <StackPanel>
        <StackPanel Orientation="Horizontal" Spacing="8">
          <PathIcon Data="{ICON_DOWNLOAD}" Width="22" Height="22" VerticalAlignment="Center"/>
          <TextBlock Text="Exports" Classes="PageTitle"/>
        </StackPanel>
        <TextBlock Text="Export snapshot data to CSV, XLSX, or JSON" Classes="PageSubtitle"/>
      </StackPanel>
    </Border>

    <!-- Main Content -->
    <ScrollViewer Grid.Row="1" Padding="24,16">
      <StackPanel Spacing="24">

        <!-- Loading State -->
        <StackPanel IsVisible="{{Binding IsLoading}}"
                    HorizontalAlignment="Center" Spacing="12" Margin="0,24">
          <ProgressBar IsIndeterminate="True" Width="200"/>
          <TextBlock Text="Loading customers\u2026" HorizontalAlignment="Center"
                     Foreground="{{DynamicResource SystemBaseMediumColor}}"/>
        </StackPanel>

        <!-- Empty State -->
        <StackPanel IsVisible="{{Binding IsEmpty}}"
                    HorizontalAlignment="Center" VerticalAlignment="Center"
                    Spacing="8" Margin="0,32">
          <PathIcon Data="{ICON_DOWNLOAD}" Width="48" Height="48" HorizontalAlignment="Center"/>
          <TextBlock Text="No customers available" Classes="EmptyStateTitle"/>
          <TextBlock Text="Create a customer and capture snapshots before exporting."
                     Classes="EmptyStateSubtitle"/>
        </StackPanel>

        <!-- Configuration Section (visible when customers are loaded) -->
        <StackPanel Spacing="20"
                    IsVisible="{{Binding !IsLoading}}">

          <!-- Customer Picker -->
          <StackPanel Spacing="4">
            <TextBlock Text="Customer" Classes="FormLabel"/>
            <ComboBox ItemsSource="{{Binding Customers}}"
                      SelectedItem="{{Binding SelectedCustomer}}"
                      PlaceholderText="Select a customer\u2026"
                      HorizontalAlignment="Stretch">
              <ComboBox.ItemTemplate>
                <DataTemplate x:CompileBindings="False">
                  <TextBlock Text="{{Binding Name}}"/>
                </DataTemplate>
              </ComboBox.ItemTemplate>
            </ComboBox>
          </StackPanel>

          <!-- Snapshot Picker -->
          <StackPanel Spacing="4">
            <TextBlock Text="Snapshot" Classes="FormLabel"/>
            <Panel>
              <ComboBox ItemsSource="{{Binding Snapshots}}"
                        SelectedItem="{{Binding SelectedSnapshot}}"
                        PlaceholderText="Select a snapshot\u2026"
                        IsEnabled="{{Binding SelectedCustomer, Converter={{x:Static ObjectConverters.IsNotNull}}}}"
                        HorizontalAlignment="Stretch">
                <ComboBox.ItemTemplate>
                  <DataTemplate x:CompileBindings="False">
                    <StackPanel Orientation="Horizontal" Spacing="8">
                      <TextBlock Text="{{Binding Name}}"/>
                      <TextBlock Text="{{Binding Status}}"
                                 Foreground="{{DynamicResource SystemBaseMediumColor}}"
                                 FontSize="12"
                                 VerticalAlignment="Center"/>
                    </StackPanel>
                  </DataTemplate>
                </ComboBox.ItemTemplate>
              </ComboBox>
              <ProgressBar IsIndeterminate="True" Height="2"
                           VerticalAlignment="Bottom"
                           IsVisible="{{Binding IsLoadingSnapshots}}"/>
            </Panel>
          </StackPanel>

          <!-- Export Format Cards -->
          <StackPanel Spacing="4">
            <TextBlock Text="Export Format" Classes="FormLabel"/>
            <StackPanel Orientation="Horizontal" Spacing="12">
              <RadioButton GroupName="ExportFormat"
                           IsChecked="{{Binding SelectedFormat, Converter={{x:Static ObjectConverters.Equal}}, ConverterParameter={{x:Static enums:ExportFormat.Csv}}}}">
                <Border Classes="ActionCard" Padding="16,12" MinWidth="100">
                  <StackPanel HorizontalAlignment="Center" Spacing="4">
                    <PathIcon Data="{ICON_DOCUMENT}" Width="24" Height="24" HorizontalAlignment="Center"/>
                    <TextBlock Text="CSV" FontWeight="SemiBold" HorizontalAlignment="Center"/>
                    <TextBlock Text="Comma-separated" FontSize="11"
                               Foreground="{{DynamicResource SystemBaseMediumColor}}"
                               HorizontalAlignment="Center"/>
                  </StackPanel>
                </Border>
              </RadioButton>

              <RadioButton GroupName="ExportFormat"
                           IsChecked="{{Binding SelectedFormat, Converter={{x:Static ObjectConverters.Equal}}, ConverterParameter={{x:Static enums:ExportFormat.Xlsx}}}}">
                <Border Classes="ActionCard" Padding="16,12" MinWidth="100">
                  <StackPanel HorizontalAlignment="Center" Spacing="4">
                    <PathIcon Data="{ICON_DOCUMENT}" Width="24" Height="24" HorizontalAlignment="Center"/>
                    <TextBlock Text="XLSX" FontWeight="SemiBold" HorizontalAlignment="Center"/>
                    <TextBlock Text="Excel workbook" FontSize="11"
                               Foreground="{{DynamicResource SystemBaseMediumColor}}"
                               HorizontalAlignment="Center"/>
                  </StackPanel>
                </Border>
              </RadioButton>

              <RadioButton GroupName="ExportFormat"
                           IsChecked="{{Binding SelectedFormat, Converter={{x:Static ObjectConverters.Equal}}, ConverterParameter={{x:Static enums:ExportFormat.Json}}}}">
                <Border Classes="ActionCard" Padding="16,12" MinWidth="100">
                  <StackPanel HorizontalAlignment="Center" Spacing="4">
                    <PathIcon Data="{ICON_DOCUMENT}" Width="24" Height="24" HorizontalAlignment="Center"/>
                    <TextBlock Text="JSON" FontWeight="SemiBold" HorizontalAlignment="Center"/>
                    <TextBlock Text="Structured data" FontSize="11"
                               Foreground="{{DynamicResource SystemBaseMediumColor}}"
                               HorizontalAlignment="Center"/>
                  </StackPanel>
                </Border>
              </RadioButton>
            </StackPanel>
          </StackPanel>

          <!-- Output Path -->
          <StackPanel Spacing="4">
            <TextBlock Text="Output Path" Classes="FormLabel"/>
            <Grid ColumnDefinitions="*,Auto" HorizontalAlignment="Stretch">
              <TextBox Grid.Column="0" Text="{{Binding OutputPath}}"
                       Watermark="Select output directory\u2026"
                       Classes="FormInput"/>
              <Button Grid.Column="1" Classes="SecondaryButton"
                      Content="Browse\u2026"
                      Command="{{Binding BrowseCommand}}"
                      Margin="8,0,0,0"/>
            </Grid>
          </StackPanel>

          <!-- Export Button -->
          <Button Classes="PrimaryButton"
                  HorizontalAlignment="Left"
                  IsEnabled="{{Binding CanExport}}"
                  Command="{{Binding ExportCommand}}"
                  MinWidth="160" Padding="24,10">
            <Panel>
              <StackPanel Orientation="Horizontal" Spacing="8"
                          IsVisible="{{Binding !IsExporting}}">
                <PathIcon Data="{ICON_DOWNLOAD}" Width="14" Height="14" VerticalAlignment="Center"/>
                <TextBlock Text="Export" VerticalAlignment="Center"/>
              </StackPanel>
              <StackPanel Orientation="Horizontal" Spacing="8"
                          IsVisible="{{Binding IsExporting}}">
                <ProgressBar IsIndeterminate="True" Width="16" Height="16"/>
                <TextBlock Text="Exporting\u2026" VerticalAlignment="Center"/>
              </StackPanel>
            </Panel>
          </Button>

          <!-- Result Panel -->
          <!-- Success -->
          <suki:InfoBar IsOpen="{{Binding IsResultSuccess}}"
                      Severity="Success" Title="Export completed successfully" IsClosable="True"
                      Message="{{Binding ResultFilePath, StringFormat='File: {{0}}'}}"/>
          <StackPanel IsVisible="{{Binding IsResultSuccess}}" Spacing="4" Margin="0,4,0,0">
            <TextBlock Text="{{Binding ResultSizeDisplay, StringFormat='Size: {{0}}'}}"
                       FontSize="12"
                       Foreground="{{DynamicResource SystemBaseMediumColor}}"/>
            <Button Classes="SecondaryButton" Content="Open File"
                    Command="{{Binding OpenFileCommand}}"
                    HorizontalAlignment="Left"/>
          </StackPanel>

          <!-- Error -->
          <suki:InfoBar IsOpen="{{Binding IsResultError}}"
                      Severity="Error" Title="Export Failed" Message="{{Binding ResultErrorMessage}}" IsClosable="True"/>

          <!-- Divider -->
          <Border Classes="Divider" IsVisible="{{Binding HasHistory}}"/>

          <!-- Export History -->
          <StackPanel Spacing="12" IsVisible="{{Binding HasHistory}}">
            <TextBlock Text="Export History" Classes="SectionTitle"/>

            <ItemsControl ItemsSource="{{Binding ExportHistory}}">
              <ItemsControl.ItemTemplate>
                <DataTemplate x:CompileBindings="False">
                  <Border Classes="CustomerCard" Margin="0,0,0,8" Padding="12">
                    <Grid ColumnDefinitions="*,Auto">
                      <StackPanel Grid.Column="0" Spacing="2">
                        <StackPanel Orientation="Horizontal" Spacing="8">
                          <TextBlock Text="{{Binding SnapshotName}}" Classes="CardTitle"/>
                          <Border Classes="BadgeSuccess" IsVisible="{{Binding IsSuccess}}">
                            <TextBlock Text="{{Binding StatusDisplay}}" Classes="BadgeText"/>
                          </Border>
                          <Border Classes="BadgeError" IsVisible="{{Binding !IsSuccess}}">
                            <TextBlock Text="{{Binding StatusDisplay}}" Classes="BadgeText"/>
                          </Border>
                          <Border Classes="BadgeInfo">
                            <TextBlock Text="{{Binding FormatDisplay}}" Classes="BadgeText"/>
                          </Border>
                        </StackPanel>
                        <TextBlock Text="{{Binding OutputPath}}" Classes="CardSubtitle"
                                   IsVisible="{{Binding IsSuccess}}" TextTrimming="CharacterEllipsis"/>
                        <TextBlock Text="{{Binding ErrorMessage}}" Classes="CardSubtitle"
                                   Foreground="#C0392B" IsVisible="{{Binding IsFailed}}"/>
                        <StackPanel Orientation="Horizontal" Spacing="12">
                          <TextBlock Classes="CardMeta"
                                     Text="{{Binding ExportedAt, StringFormat='{{}}{{0:MMM d, yyyy HH:mm}}'}}"/>
                          <TextBlock Classes="CardMeta" Text="{{Binding SizeDisplay}}"
                                     IsVisible="{{Binding IsSuccess}}"/>
                        </StackPanel>
                      </StackPanel>
                      <Button Grid.Column="1" Classes="SecondaryButton" Content="Open"
                              IsVisible="{{Binding IsSuccess}}"
                              Command="{{Binding $parent[UserControl].((vm:ExportsViewModel)DataContext).OpenHistoryFileCommand}}"
                              CommandParameter="{{Binding}}"
                              VerticalAlignment="Center"/>
                    </Grid>
                  </Border>
                </DataTemplate>
              </ItemsControl.ItemTemplate>
            </ItemsControl>
          </StackPanel>

        </StackPanel>
      </StackPanel>
    </ScrollViewer>

    <!-- Error Bar -->
    <suki:InfoBar Grid.Row="2"
                IsOpen="{{Binding ErrorMessage, Converter={{x:Static StringConverters.IsNotNullOrEmpty}}}}"
                Severity="Error" Title="Error" Message="{{Binding ErrorMessage}}" IsClosable="True"
                Margin="24,0,24,16"/>

  </Grid>
</UserControl>
"""

# ============================================================
# Write all files
# ============================================================
for path, content in files.items():
    with open(path, 'w', encoding='utf-8') as f:
        f.write(content)
    print(f"Updated: {os.path.relpath(path, BASE)}")

print(f"\nAll {len(files)} AXAML files migrated successfully.")
