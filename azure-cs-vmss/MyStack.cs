using Pulumi;
using Pulumi.Azure.Core;
using Pulumi.Azure.Compute;
using Pulumi.Azure.Compute.Inputs;
using Pulumi.Azure.Network;
using Pulumi.Azure.Network.Inputs;

class MyStack : Stack
{
    public MyStack()
    {
        var stackId = "webScaleSet";

        // Create an Azure Resource Group
        var resourceGroup = new ResourceGroup($"{stackId}-rg");

        // Create Networking components
        var vnet = new VirtualNetwork($"{stackId}-vnet", new VirtualNetworkArgs
        {
            ResourceGroupName = resourceGroup.Name,
            AddressSpaces = new InputList<string>{
                "10.0.0.0/16"
            }
        });

        var privateSubnet = new Subnet($"{stackId}-privateSubnet", new SubnetArgs
        {
            ResourceGroupName = resourceGroup.Name,
            AddressPrefix = "10.0.2.0/24",
            VirtualNetworkName = vnet.Name
        });
        
        var publicSubnet = new Subnet($"{stackId}-publicSubnet", new SubnetArgs
        {
            ResourceGroupName = resourceGroup.Name,
            AddressPrefix = "10.0.1.0/24",
            VirtualNetworkName = vnet.Name
        });

        // Create a LoadBalancer with a public ip
        var publicIp = new PublicIp($"{stackId}-pip", new PublicIpArgs
        {
            ResourceGroupName = resourceGroup.Name,
            Sku = "Basic",
            AllocationMethod = "Dynamic",
            DomainNameLabel = "aspnettodo"
        }, new CustomResourceOptions{DeleteBeforeReplace=true});
        
        var appGw = new ApplicationGateway($"{stackId}-appgw", new ApplicationGatewayArgs
        {
            ResourceGroupName = resourceGroup.Name,
            Sku = new ApplicationGatewaySkuArgs
            {
                Tier = "Standard",
                Name = "Standard_Small",
                Capacity = 1
            },
            FrontendIpConfigurations = new InputList<Pulumi.Azure.Network.Inputs.ApplicationGatewayFrontendIpConfigurationsArgs>
            {
                new Pulumi.Azure.Network.Inputs.ApplicationGatewayFrontendIpConfigurationsArgs
                {
                    Name = $"{stackId}-appgw-ipconfig-0",
                    PublicIpAddressId = publicIp.Id,
                }
            },
            FrontendPorts = new InputList<Pulumi.Azure.Network.Inputs.ApplicationGatewayFrontendPortsArgs>
            {
                new Pulumi.Azure.Network.Inputs.ApplicationGatewayFrontendPortsArgs
                {
                    Name = "Port80",
                    Port = 80
                }
            },
            BackendAddressPools = new InputList<Pulumi.Azure.Network.Inputs.ApplicationGatewayBackendAddressPoolsArgs>
            {
                new Pulumi.Azure.Network.Inputs.ApplicationGatewayBackendAddressPoolsArgs
                {
                    Name = $"{stackId}-bepool-0",
                }
            },
            BackendHttpSettings = new InputList<ApplicationGatewayBackendHttpSettingsArgs>
            {
                new ApplicationGatewayBackendHttpSettingsArgs
                {
                    Name = "HTTPSettings",
                    Protocol = "HTTP",
                    Port = 80,
                    CookieBasedAffinity = "Disabled"
                }
            },
            GatewayIpConfigurations = new InputList<ApplicationGatewayGatewayIpConfigurationsArgs>
            {
                new ApplicationGatewayGatewayIpConfigurationsArgs
                {
                    Name = "IPConfiguration",
                    SubnetId = publicSubnet.Id
                }
            },
            HttpListeners = new InputList<ApplicationGatewayHttpListenersArgs>
            {
                new ApplicationGatewayHttpListenersArgs
                {
                    Name = "HTTPListener",
                    Protocol = "HTTP",
                    FrontendIpConfigurationName = $"{stackId}-appgw-ipconfig-0",
                    FrontendPortName = "Port80"
                }
            },
            RequestRoutingRules = new InputList<ApplicationGatewayRequestRoutingRulesArgs>
            {
                new ApplicationGatewayRequestRoutingRulesArgs
                {
                    Name = "Default",
                    BackendAddressPoolName = $"{stackId}-bepool-0",
                    HttpListenerName = "HTTPListener",
                    RuleType = "Basic",
                    BackendHttpSettingsName = "HTTPSettings"
                }
            }
            
        });

        
        
        var scaleSet = new ScaleSet($"{stackId}-vmss", new ScaleSetArgs
        {
            ResourceGroupName = resourceGroup.Name,
            Location = resourceGroup.Location,
            Zones = new InputList<string> {
                "1","2"
            },
            NetworkProfiles =
            {
                new ScaleSetNetworkProfilesArgs
                {
                    AcceleratedNetworking = false,
                    IpConfigurations =
                    {
                        new ScaleSetNetworkProfilesIpConfigurationsArgs
                        {
                            Name = "IPConfiguration",
                            Primary = true,
                            SubnetId = privateSubnet.Id,
                            ApplicationGatewayBackendAddressPoolIds = new InputList<string>
                            {
                                appGw.BackendAddressPools.Apply(bePools => bePools[0].Id)
                            }
                        }
                    },
                    Name = "networkprofile",
                    Primary = true
                }
            },
            OsProfile = new ScaleSetOsProfileArgs
            {
                AdminUsername = "webadmin",
                AdminPassword = "SEcurePwd$3",
                ComputerNamePrefix = "web"
            },
            OsProfileWindowsConfig = new ScaleSetOsProfileWindowsConfigArgs
            {
                ProvisionVmAgent = true
            },
            Sku = new ScaleSetSkuArgs
            {
                Capacity = 2,
                Name = "Standard_B1s",
                Tier = "Standard",
            },

            StorageProfileImageReference = new ScaleSetStorageProfileImageReferenceArgs
            {
                Offer = "WindowsServer",
                Publisher = "MicrosoftWindowsServer",
                Sku = "2019-Datacenter-Core",
                Version = "latest",
            },
            StorageProfileOsDisk = new ScaleSetStorageProfileOsDiskArgs
            {
                Caching = "ReadWrite",
                CreateOption = "FromImage",
                ManagedDiskType = "Standard_LRS",
                Name = "",
            },
            UpgradePolicyMode = "Automatic",
            Extensions = new InputList<ScaleSetExtensionsArgs>
            {
                new ScaleSetExtensionsArgs
                {
                    Publisher = "Microsoft.Compute",
                    Name = "IIS-Script-Extension",
                    Type = "CustomScriptExtension",
                    TypeHandlerVersion = "1.4",
                    Settings = "{\"commandToExecute\":\"powershell Add-WindowsFeature Web-Server,Web-Asp-Net45,NET-Framework-Features\"}"
                }
            }
        });
        
        this.PublicUrl = publicIp.Fqdn;
    }

    [Output]
    public Output<string> PublicUrl { get; set; }
}
