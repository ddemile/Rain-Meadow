﻿using System;

namespace RainMeadow
{
    public abstract partial class OnlineResource
    {
        // I request this resource, so I can have either ownership or subscription
        public void Request()
        {
            RainMeadow.Debug(this);
            if (isPending) throw new InvalidOperationException("pending");
            if (isAvailable) throw new InvalidOperationException("available");

            ClearIncommingBuffers();
            pendingRequest = supervisor.InvokeRPC(this.Requested).Then(this.ResolveRequest);
        }

        // I no longer need this resource, supervisor can coordinate its transfer if needed
        private void Release()
        {
            RainMeadow.Debug(this);
            if (isPending) throw new InvalidOperationException("pending");
            if (!isAvailable) throw new InvalidOperationException("not available");
            if (!canRelease) throw new InvalidOperationException("cant be released in current state");

            pendingRequest = supervisor.InvokeRPC(this.Released).Then(this.ResolveRelease);
        }

        // Someone requested this resource, if I supervise it I'll lease it
        [RPCMethod]
        public void Requested(RPCEvent request)
        {
            RainMeadow.Debug(this);
            if (isSupervisor)
            {
                if (participants.ContainsKey(request.from)) // they are already in this
                {
                    request.from.QueueEvent(new GenericResult.Error(request));
                    return;
                }

                if (isFree)
                {
                    // Leased to player
                    request.from.QueueEvent(new GenericResult.Ok(request));
                    NewOwner(request.from);
                    return;
                }
                else
                {
                    // Already leased, player subscribed
                    request.from.QueueEvent(new GenericResult.Ok(request));
                    NewParticipant(request.from);
                    return;
                }
            }

            request.from.QueueEvent(new GenericResult.Error(request));
        }

        // Someone is trying to release this resource, if I supervise it, I'll handle it
        [RPCMethod]
        public void Released(RPCEvent request)
        {
            RainMeadow.Debug(this);
            if (isSupervisor)
            {
                if (!participants.ContainsKey(request.from)) // they are already out?
                {
                    request.from.QueueEvent(new GenericResult.Error(request));
                    return;
                }

                if (request.from == owner) // Owner left, might need a transfer
                {
                    request.from.QueueEvent(new GenericResult.Ok(request)); // this notifies the old owner that the release was a success
                    ParticipantLeft(request.from);
                    var newOwner = MatchmakingManager.instance.BestTransferCandidate(this, participants);
                    NewOwner(newOwner); // This notifies all users, if the new owner is active they'll restore the state
                    if (newOwner != null)
                    {
                        newOwner.InvokeRPC(this.Transfered);
                    }
                    return;
                }
                else
                {
                    request.from.QueueEvent(new GenericResult.Ok(request)); // non-owner unsubscribed
                    ParticipantLeft(request.from);
                    return;
                }
            }
            request.from.QueueEvent(new GenericResult.Error(request)); // I do not manage this resource
        }

        // The previous owner has left and I've been assigned (by super) as the new owner
        [RPCMethod]
        public void Transfered(RPCEvent request)
        {
            RainMeadow.Debug(this);
            if (isAvailable && isActive && request.from == supervisor) // I am a subscriber with a valid state who now owns this resource
            {
                request.from.QueueEvent(new GenericResult.Ok(request));
                return;
            }

            RainMeadow.Debug($"Transfer error : {isAvailable} {isActive} {request.from == supervisor}");
            request.from.QueueEvent(new GenericResult.Error(request)); // super should retry with someone else
        }

        // A pending request was answered to
        public void ResolveRequest(GenericResult requestResult)
        {
            RainMeadow.Debug(this);
            if (requestResult.referencedEvent == pendingRequest) pendingRequest = null;
            else RainMeadow.Debug($"Weird event situation, pending is {pendingRequest} and referenced is {requestResult.referencedEvent}");

            if (requestResult is GenericResult.Ok)
            {
                if (isAvailable) // this was transfered to me because the previous owner left
                {
                    RainMeadow.Debug("Claimed abandoned resource");
                }
                else
                {
                    RainMeadow.Debug("Claimed free resource");
                    WaitingForState();
                    if (isOwner) Available();
                }
            }
            else if (requestResult is GenericResult.Error) // I should retry
            {
                // todo retry logic
                RainMeadow.Error("request failed for " + this);
            }
        }

        // A pending release was answered to
        public void ResolveRelease(GenericResult releaseResult)
        {
            RainMeadow.Debug(this);
            if (pendingRequest == releaseResult.referencedEvent) pendingRequest = null;
            else RainMeadow.Debug($"Weird event situation, pending is {pendingRequest} and referenced is {releaseResult.referencedEvent}");

            if (releaseResult is GenericResult.Ok) // I've let go
            {
                Unavailable();
            }
            else if (releaseResult is GenericResult.Error) // I should retry
            {
                // todo retry logic
                RainMeadow.Error("released failed for " + this);
            }
        }

        // A pending transfer was asnwered to
        public void ResolveTransfer(GenericResult transferResult)
        {
            RainMeadow.Debug(this);
            if (pendingRequest == transferResult.referencedEvent) pendingRequest = null;
            else RainMeadow.Debug($"Weird event situation, pending is {pendingRequest} and referenced is {transferResult.referencedEvent}");

            if (transferResult is GenericResult.Ok) // New owner accepted it
            {
                // no op
            }
            else if (transferResult is GenericResult.Error) // I should retry
            {
                // todo retry logic
                RainMeadow.Error("transfer failed for " + this);
            }
        }
    }
}
