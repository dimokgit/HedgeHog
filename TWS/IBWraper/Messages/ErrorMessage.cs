/* Copyright (C) 2013 Interactive Brokers LLC. All rights reserved.  This code is subject to the terms
 * and conditions of the IB API Non-Commercial License or the IB API Commercial License, as applicable. */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IBApp
{
     class ErrorMessage_Old : IBMessage 
    {
        private string message;
        private int errorCode;
        private int requestId;

        public ErrorMessage_Old(int requestId, int errorCode, string message):base(MessageType.Error)
        {
            Message = message;
            RequestId = requestId;
            ErrorCode = errorCode;
        }

        public string Message
        {
            get { return message; }
            set { message = value; }
        }
        
        public int ErrorCode
        {
            get { return errorCode; }
            set { errorCode = value; }
        }
        

        public int RequestId
        {
            get { return requestId; }
            set { requestId = value; }
        }

        public override string ToString()
        {
            return "Error. Request: "+RequestId+", Code: "+ErrorCode+" - "+Message;
        }
       
    }
}
