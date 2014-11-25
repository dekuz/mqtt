﻿using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Hermes;
using Hermes.Flows;
using Hermes.Packets;
using Hermes.Storage;
using Moq;
using Xunit;

namespace Tests.Flows
{
	public class UnsubscribeFlowSpec
	{
		[Fact]
		public async Task when_unsubscribing_existing_subscriptions_then_subscriptions_are_deleted_and_ack_is_sent()
		{
			var sessionRepository = new Mock<IRepository<ClientSession>> ();
			var packetIdentifierRepository = Mock.Of<IRepository<PacketIdentifier>> ();

			var flow = new UnsubscribeFlow (sessionRepository.Object, packetIdentifierRepository);

			var clientId = Guid.NewGuid ().ToString ();
			var packetId = (ushort)new Random ().Next (0, ushort.MaxValue);
			var topic = "foo/bar/test";
			var qos = QualityOfService.AtLeastOnce;
			var session = new ClientSession { 
				ClientId = clientId,
				Clean = false, 
				Subscriptions = new List<ClientSubscription> { 
					new ClientSubscription { ClientId = clientId, MaximumQualityOfService = qos, TopicFilter = topic } 
				} 
			};
			var updatedSession = default(ClientSession);

			sessionRepository.Setup (r => r.Get (It.IsAny<Expression<Func<ClientSession, bool>>> ())).Returns (session);
			sessionRepository.Setup (r => r.Update (It.IsAny<ClientSession> ())).Callback<ClientSession> (s => updatedSession = s);
			
			var unsubscribe = new Unsubscribe (packetId, topic);

			var context = new Mock<ICommunicationContext> ();

			var response = default(IPacket);

			context.Setup (c => c.PushDeliveryAsync (It.IsAny<IPacket> ()))
				.Callback<IPacket> (p => response = p)
				.Returns(Task.Delay(0));

			await flow.ExecuteAsync(clientId, unsubscribe, context.Object);

			Assert.NotNull (response);
			Assert.Equal (0, updatedSession.Subscriptions.Count);

			var unsubscribeAck = response as UnsubscribeAck;

			Assert.NotNull (unsubscribeAck);
			Assert.Equal (packetId, unsubscribeAck.PacketId);
		}

		[Fact]
		public async Task when_unsubscribing_not_existing_subscriptions_then_ack_is_sent()
		{
			var sessionRepository = new Mock<IRepository<ClientSession>> ();
			var packetIdentifierRepository = Mock.Of<IRepository<PacketIdentifier>> ();

			var flow = new UnsubscribeFlow (sessionRepository.Object, packetIdentifierRepository);

			var clientId = Guid.NewGuid ().ToString ();
			var packetId = (ushort)new Random ().Next (0, ushort.MaxValue);
			var session = new ClientSession { 
				ClientId = clientId,
				Clean = false
			};

			sessionRepository.Setup (r => r.Get (It.IsAny<Expression<Func<ClientSession, bool>>> ())).Returns (session);

			var unsubscribe = new Unsubscribe (packetId, "foo/bar");

			var context = new Mock<ICommunicationContext> ();

			var response = default(IPacket);

			context.Setup (c => c.PushDeliveryAsync (It.IsAny<IPacket> ()))
				.Callback<IPacket> (p => response = p)
				.Returns(Task.Delay(0));

			await flow.ExecuteAsync(clientId, unsubscribe, context.Object);

			sessionRepository.Verify (r => r.Delete (It.IsAny<Expression<Func<ClientSession, bool>>> ()), Times.Never);
			Assert.NotNull (response);

			var unsubscribeAck = response as UnsubscribeAck;

			Assert.NotNull (unsubscribeAck);
			Assert.Equal (packetId, unsubscribeAck.PacketId);
		}

		[Fact]
		public async Task when_sending_unsubscribe_ack_then_packet_identifier_is_deleted()
		{
			var sessionRepository = Mock.Of<IRepository<ClientSession>> ();
			var packetIdentifierRepository = new Mock<IRepository<PacketIdentifier>> ();

			var clientId = Guid.NewGuid().ToString();
			var packetId = (ushort)new Random ().Next (0, ushort.MaxValue);
			var unsubscribeAck = new UnsubscribeAck (packetId);

			var flow = new UnsubscribeFlow (sessionRepository, packetIdentifierRepository.Object);

			var context = new Mock<ICommunicationContext> ();

			var response = default(IPacket);

			context.Setup (c => c.PushDeliveryAsync (It.IsAny<IPacket> ()))
				.Callback<IPacket> (p => response = p)
				.Returns(Task.Delay(0));

			await flow.ExecuteAsync (clientId, unsubscribeAck, context.Object);

			packetIdentifierRepository.Verify (r => r.Delete (It.IsAny<Expression<Func<PacketIdentifier, bool>>> ()));
			Assert.Null (response);
		}

		[Fact]
		public void when_sending_invalid_packet_to_unsubscribe_then_fails()
		{
			var sessionRepository = Mock.Of<IRepository<ClientSession>> ();
			var packetIdentifierRepository = Mock.Of<IRepository<PacketIdentifier>> ();

			var flow = new UnsubscribeFlow (sessionRepository, packetIdentifierRepository);

			var clientId = Guid.NewGuid ().ToString ();
			var context = new Mock<ICommunicationContext> ();
			var sentPacket = default(IPacket);

			context.Setup (c => c.PushDeliveryAsync (It.IsAny<IPacket> ()))
				.Callback<IPacket> (packet => sentPacket = packet)
				.Returns(Task.Delay(0));

			var ex = Assert.Throws<AggregateException> (() => flow.ExecuteAsync (clientId, new Publish("test", QualityOfService.AtMostOnce, false, false), context.Object).Wait());

			Assert.True (ex.InnerException is ProtocolException);
		}
	}
}
