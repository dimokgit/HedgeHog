using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;
using System.Security.Permissions;

namespace HedgeHog.Shared {
  [Serializable()]
  public class WiredException : Exception, ISerializable {
    // This public constructor is used by class instantiators.
    public WiredException(string message, Exception inner) :
      base(message, inner) {
    }

    // This protected constructor is used for deserialization.
    protected WiredException(SerializationInfo info,
        StreamingContext context) :
      base(info, context) { }

    // GetObjectData performs a custom serialization.
    [SecurityPermissionAttribute(SecurityAction.Demand, SerializationFormatter = true)]
    public override void GetObjectData(SerializationInfo info,
        StreamingContext context) {
      base.GetObjectData(info, context);
    }
  }
}
