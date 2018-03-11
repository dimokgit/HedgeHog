/* Copyright (C) 2015 Interactive Brokers LLC. All rights reserved.  This code is subject to the terms
 * and conditions of the IB API Non-Commercial License or the IB API Commercial License, as applicable. */
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

namespace IBApi {
  /**
  * @brief Captures incoming messages to the API client and places them into a queue.
  */
  public class EReader {
    EClientSocket eClientSocket;
    EReaderSignal eReaderSignal;
    Queue<EMessage> msgQueue = new Queue<EMessage>();
    EDecoder processMsgsDecoder;
    const int defaultInBufSize = ushort.MaxValue / 8;
    public Task MessageQueueThread;

    bool UseV100Plus {
      get {
        return eClientSocket.UseV100Plus;
      }
    }


    static EWrapper defaultWrapper = new DefaultEWrapper();

    public EReader(EClientSocket clientSocket, EReaderSignal signal) {
      eClientSocket = clientSocket;
      eReaderSignal = signal;
      processMsgsDecoder = new EDecoder(eClientSocket.ServerVersion, eClientSocket.Wrapper, eClientSocket);
    }

    public void Start() {
      var t = new Thread(() => {
        while(eClientSocket.IsConnected())
          if(!putMessageToQueue())
            break;
      });
      t.IsBackground = true;
      t.Start();
      return;
      if(MessageQueueThread?.Status == TaskStatus.Running)
        throw new Exception(new { MessageQueueThread = new { MessageQueueThread.Status } } + "");
      MessageQueueThread = Task.Factory.StartNew(() => {
        while(eClientSocket.IsConnected())
          if(!putMessageToQueue())
            break;
      }, TaskCreationOptions.LongRunning);
      MessageQueueThread.ContinueWith(_ => eReaderSignal.issueSignal());
    }

    EMessage getMsg() {
      lock(msgQueue)
        return msgQueue.Count == 0 ? null : msgQueue.Dequeue();
    }

    public void processMsgs() {
      EMessage msg = getMsg();

      while(msg != null && processMsgsDecoder.ParseAndProcessMsg(msg.GetBuf()) > 0)
        msg = getMsg();
    }

    public bool putMessageToQueue() {
      try {
        EMessage msg = readSingleMessage();

        if(msg == null)
          return false;

        lock(msgQueue)
          msgQueue.Enqueue(msg);

        eReaderSignal.issueSignal();

        return true;
      } catch(Exception ex) {
        if(eClientSocket.IsConnected())
          eClientSocket.Wrapper.error(ex);

        return false;
      }
    }

    List<byte> inBuf = new List<byte>(defaultInBufSize);

    private EMessage readSingleMessage() {
      var msgSize = 0;

      if(UseV100Plus) {
        msgSize = eClientSocket.ReadInt();

        if(msgSize > Constants.MaxMsgSize) {
          throw new EClientException(EClientErrors.BAD_LENGTH);
        }

        var buf = new List<byte>();
        var offset = 0;

        while(offset < msgSize) {
          var readBuf = eClientSocket.ReadByteArray(msgSize - offset);

          buf.AddRange(readBuf);

          offset += readBuf.Length;
        }

        return new EMessage(buf.ToArray());
      }

      if(inBuf.Count == 0)
        AppendInBuf();

      while(true)
        try {
          msgSize = new EDecoder(this.eClientSocket.ServerVersion, defaultWrapper).ParseAndProcessMsg(inBuf.ToArray());
          break;
        } catch(EndOfStreamException) {
          if(inBuf.Count >= inBuf.Capacity * 3 / 4)
            inBuf.Capacity *= 2;

          AppendInBuf();
        }

      var msgBuf = new byte[msgSize];

      inBuf.CopyTo(0, msgBuf, 0, msgSize);
      inBuf.RemoveRange(0, msgSize);

      if(inBuf.Count < defaultInBufSize && inBuf.Capacity > defaultInBufSize)
        inBuf.Capacity = defaultInBufSize;

      return new EMessage(msgBuf);
    }

    private void AppendInBuf() {
      inBuf.AddRange(eClientSocket.ReadByteArray(inBuf.Capacity - inBuf.Count));
    }
  }
}
