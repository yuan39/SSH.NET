﻿using System;
using System.IO;
using System.Linq;
using System.Threading;
using Renci.SshClient.Messages.Connection;

namespace Renci.SshClient.Channels
{
    //  TODO:   Add Begin* and End* methods for async calls

    internal class ChannelExec : Channel
    {
        private Stream _channelData;

        private Stream _channelExtendedData;

        private Exception _exception;

        private ChannelAsyncResult _asyncResult;

        private AsyncCallback _callback;

        public override ChannelTypes ChannelType
        {
            get { return ChannelTypes.Session; }
        }

        public ChannelExec(Session session)
            : base(session, 0x100000, 0x1000)
        {
        }

        internal ChannelAsyncResult BeginExecute(string command, Stream output, Stream extendedOutput, AsyncCallback callback, object state)
        {
            //  Prevent from executing BeginExecute before calling EndExecute
            if (this._asyncResult != null)
            {
                throw new InvalidOperationException("");
            }

            //  Create new AsyncResult object
            this._asyncResult = new ChannelAsyncResult(this)
            {
                AsyncWaitHandle = new EventWaitHandle(false, EventResetMode.ManualReset),
                IsCompleted = false,
                AsyncState = state,
            };

            this._callback = callback;

            this._channelData = output;
            this._channelExtendedData = extendedOutput;

            this.Open();

            //  Send channel command request
            this.SendMessage(new ChannelRequestMessage
            {
                ChannelNumber = this.ServerChannelNumber,
                RequestName = RequestNames.Exec,
                WantReply = false,
                Command = command,
            });

            return _asyncResult;
        }

        internal void EndExecute(IAsyncResult result)
        {
            ChannelAsyncResult channelAsyncResult = result as ChannelAsyncResult;

            if (channelAsyncResult.Channel != this)
            {
                throw new InvalidOperationException("Invalid IAsyncResult parameter");
            }

            //Make sure that operation completed if not wait for it to finish
            this._asyncResult.AsyncWaitHandle.WaitOne();

            this.Close();

            this._asyncResult = null;

            if (this._exception != null)
            {
                var exception = this._exception;
                this._exception = null; //  Clean exception
                throw exception;
            }
        }

        protected override void OnChannelEof()
        {
            base.OnChannelEof();

            this.ExecutionCompleted();
        }

        protected override void OnChannelFailed(uint reasonCode, string description)
        {
            base.OnChannelFailed(reasonCode, description);
            this._exception = new InvalidOperationException(string.Format("Channel failed to open. Code: {0}, Reason {1}", reasonCode, description));
            this.ExecutionCompleted();
        }

        protected override void OnChannelData(string data)
        {
            base.OnChannelData(data);

            if (this._channelData != null)
            {
                this._channelData.Write(data.GetSshBytes().ToArray(), 0, data.Length);
            }

            if (this._asyncResult != null)
            {
                this._asyncResult.BytesReceived += data.Length;
            }
        }

        protected override void OnChannelExtendedData(string data, uint dataTypeCode)
        {
            base.OnChannelExtendedData(data, dataTypeCode);

            //  TODO:   dataTypeCode curentlyu ignored
            if (this._channelExtendedData != null)
            {
                this._channelExtendedData.Write(data.GetSshBytes().ToArray(), 0, data.Length);
            }
        }

        private void ExecutionCompleted()
        {
            this._asyncResult.IsCompleted = true;
            if (this._callback != null)
            {
                this._callback(this._asyncResult);
            }
            ((EventWaitHandle)_asyncResult.AsyncWaitHandle).Set();
        }
    }
}