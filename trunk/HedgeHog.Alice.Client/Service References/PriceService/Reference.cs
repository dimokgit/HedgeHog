﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.1
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace HedgeHog.Alice.Client.PriceService {
    
    
    [System.CodeDom.Compiler.GeneratedCodeAttribute("System.ServiceModel", "4.0.0.0")]
    [System.ServiceModel.ServiceContractAttribute(ConfigurationName="PriceService.IPriceService")]
    public interface IPriceService {
        
        [System.ServiceModel.OperationContractAttribute(Action="http://tempuri.org/IPriceService/FillPrice", ReplyAction="http://tempuri.org/IPriceService/FillPriceResponse")]
        HedgeHog.Bars.Rate[] FillPrice(string pair, System.DateTime startDate);
        
        [System.ServiceModel.OperationContractAttribute(Action="http://tempuri.org/IPriceService/AddPair", ReplyAction="http://tempuri.org/IPriceService/AddPairResponse")]
        bool AddPair(string pair);
    }
    
    [System.CodeDom.Compiler.GeneratedCodeAttribute("System.ServiceModel", "4.0.0.0")]
    public interface IPriceServiceChannel : HedgeHog.Alice.Client.PriceService.IPriceService, System.ServiceModel.IClientChannel {
    }
    
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.CodeDom.Compiler.GeneratedCodeAttribute("System.ServiceModel", "4.0.0.0")]
    public partial class PriceServiceClient : System.ServiceModel.ClientBase<HedgeHog.Alice.Client.PriceService.IPriceService>, HedgeHog.Alice.Client.PriceService.IPriceService {
        
        public PriceServiceClient() {
        }
        
        public PriceServiceClient(string endpointConfigurationName) : 
                base(endpointConfigurationName) {
        }
        
        public PriceServiceClient(string endpointConfigurationName, string remoteAddress) : 
                base(endpointConfigurationName, remoteAddress) {
        }
        
        public PriceServiceClient(string endpointConfigurationName, System.ServiceModel.EndpointAddress remoteAddress) : 
                base(endpointConfigurationName, remoteAddress) {
        }
        
        public PriceServiceClient(System.ServiceModel.Channels.Binding binding, System.ServiceModel.EndpointAddress remoteAddress) : 
                base(binding, remoteAddress) {
        }
        
        public HedgeHog.Bars.Rate[] FillPrice(string pair, System.DateTime startDate) {
            return base.Channel.FillPrice(pair, startDate);
        }
        
        public bool AddPair(string pair) {
            return base.Channel.AddPair(pair);
        }
    }
}
