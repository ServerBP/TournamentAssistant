// @generated by protobuf-ts 2.8.2
// @generated from protobuf file "packets.proto" (package "proto.packets", syntax proto3)
// tslint:disable
import type { BinaryWriteOptions } from "@protobuf-ts/runtime";
import type { IBinaryWriter } from "@protobuf-ts/runtime";
import { WireType } from "@protobuf-ts/runtime";
import type { BinaryReadOptions } from "@protobuf-ts/runtime";
import type { IBinaryReader } from "@protobuf-ts/runtime";
import { UnknownFieldHandler } from "@protobuf-ts/runtime";
import type { PartialMessage } from "@protobuf-ts/runtime";
import { reflectionMergePartial } from "@protobuf-ts/runtime";
import { MESSAGE_TYPE } from "@protobuf-ts/runtime";
import { MessageType } from "@protobuf-ts/runtime";
import { Event } from './events.js';
import { Response } from './responses.js';
import { Request } from './requests.js';
import { Push } from './pushes.js';
import { Command } from './commands.js';
/**
 * ---- Backbone ---- //
 *
 * @generated from protobuf message proto.packets.Acknowledgement
 */
export interface Acknowledgement {
    /**
     * @generated from protobuf field: string packet_id = 1;
     */
    packetId: string;
    /**
     * @generated from protobuf field: proto.packets.Acknowledgement.AcknowledgementType type = 2;
     */
    type: Acknowledgement_AcknowledgementType;
}
/**
 * @generated from protobuf enum proto.packets.Acknowledgement.AcknowledgementType
 */
export enum Acknowledgement_AcknowledgementType {
    /**
     * @generated from protobuf enum value: MessageReceived = 0;
     */
    MessageReceived = 0
}
/**
 * @generated from protobuf message proto.packets.ForwardingPacket
 */
export interface ForwardingPacket {
    /**
     * @generated from protobuf field: repeated string forward_to = 1;
     */
    forwardTo: string[];
    /**
     * @generated from protobuf field: proto.packets.Packet packet = 2;
     */
    packet?: Packet;
}
/**
 * @generated from protobuf message proto.packets.Packet
 */
export interface Packet {
    /**
     * @generated from protobuf field: string token = 1;
     */
    token: string;
    /**
     * @generated from protobuf field: string id = 2;
     */
    id: string;
    /**
     * @generated from protobuf field: string from = 3;
     */
    from: string;
    /**
     * @generated from protobuf oneof: packet
     */
    packet: {
        oneofKind: "acknowledgement";
        /**
         * @generated from protobuf field: proto.packets.Acknowledgement acknowledgement = 4;
         */
        acknowledgement: Acknowledgement;
    } | {
        oneofKind: "forwardingPacket";
        /**
         * @generated from protobuf field: proto.packets.ForwardingPacket forwarding_packet = 5;
         */
        forwardingPacket: ForwardingPacket;
    } | {
        oneofKind: "command";
        /**
         * @generated from protobuf field: proto.packets.Command command = 6;
         */
        command: Command;
    } | {
        oneofKind: "push";
        /**
         * @generated from protobuf field: proto.packets.Push push = 7;
         */
        push: Push;
    } | {
        oneofKind: "request";
        /**
         * @generated from protobuf field: proto.packets.Request request = 8;
         */
        request: Request;
    } | {
        oneofKind: "response";
        /**
         * @generated from protobuf field: proto.packets.Response response = 9;
         */
        response: Response;
    } | {
        oneofKind: "event";
        /**
         * @generated from protobuf field: proto.packets.Event event = 10;
         */
        event: Event;
    } | {
        oneofKind: undefined;
    };
}
// @generated message type with reflection information, may provide speed optimized methods
class Acknowledgement$Type extends MessageType<Acknowledgement> {
    constructor() {
        super("proto.packets.Acknowledgement", [
            { no: 1, name: "packet_id", kind: "scalar", T: 9 /*ScalarType.STRING*/ },
            { no: 2, name: "type", kind: "enum", T: () => ["proto.packets.Acknowledgement.AcknowledgementType", Acknowledgement_AcknowledgementType] }
        ]);
    }
    create(value?: PartialMessage<Acknowledgement>): Acknowledgement {
        const message = { packetId: "", type: 0 };
        globalThis.Object.defineProperty(message, MESSAGE_TYPE, { enumerable: false, value: this });
        if (value !== undefined)
            reflectionMergePartial<Acknowledgement>(this, message, value);
        return message;
    }
    internalBinaryRead(reader: IBinaryReader, length: number, options: BinaryReadOptions, target?: Acknowledgement): Acknowledgement {
        let message = target ?? this.create(), end = reader.pos + length;
        while (reader.pos < end) {
            let [fieldNo, wireType] = reader.tag();
            switch (fieldNo) {
                case /* string packet_id */ 1:
                    message.packetId = reader.string();
                    break;
                case /* proto.packets.Acknowledgement.AcknowledgementType type */ 2:
                    message.type = reader.int32();
                    break;
                default:
                    let u = options.readUnknownField;
                    if (u === "throw")
                        throw new globalThis.Error(`Unknown field ${fieldNo} (wire type ${wireType}) for ${this.typeName}`);
                    let d = reader.skip(wireType);
                    if (u !== false)
                        (u === true ? UnknownFieldHandler.onRead : u)(this.typeName, message, fieldNo, wireType, d);
            }
        }
        return message;
    }
    internalBinaryWrite(message: Acknowledgement, writer: IBinaryWriter, options: BinaryWriteOptions): IBinaryWriter {
        /* string packet_id = 1; */
        if (message.packetId !== "")
            writer.tag(1, WireType.LengthDelimited).string(message.packetId);
        /* proto.packets.Acknowledgement.AcknowledgementType type = 2; */
        if (message.type !== 0)
            writer.tag(2, WireType.Varint).int32(message.type);
        let u = options.writeUnknownFields;
        if (u !== false)
            (u == true ? UnknownFieldHandler.onWrite : u)(this.typeName, message, writer);
        return writer;
    }
}
/**
 * @generated MessageType for protobuf message proto.packets.Acknowledgement
 */
export const Acknowledgement = new Acknowledgement$Type();
// @generated message type with reflection information, may provide speed optimized methods
class ForwardingPacket$Type extends MessageType<ForwardingPacket> {
    constructor() {
        super("proto.packets.ForwardingPacket", [
            { no: 1, name: "forward_to", kind: "scalar", repeat: 2 /*RepeatType.UNPACKED*/, T: 9 /*ScalarType.STRING*/ },
            { no: 2, name: "packet", kind: "message", T: () => Packet }
        ]);
    }
    create(value?: PartialMessage<ForwardingPacket>): ForwardingPacket {
        const message = { forwardTo: [] };
        globalThis.Object.defineProperty(message, MESSAGE_TYPE, { enumerable: false, value: this });
        if (value !== undefined)
            reflectionMergePartial<ForwardingPacket>(this, message, value);
        return message;
    }
    internalBinaryRead(reader: IBinaryReader, length: number, options: BinaryReadOptions, target?: ForwardingPacket): ForwardingPacket {
        let message = target ?? this.create(), end = reader.pos + length;
        while (reader.pos < end) {
            let [fieldNo, wireType] = reader.tag();
            switch (fieldNo) {
                case /* repeated string forward_to */ 1:
                    message.forwardTo.push(reader.string());
                    break;
                case /* proto.packets.Packet packet */ 2:
                    message.packet = Packet.internalBinaryRead(reader, reader.uint32(), options, message.packet);
                    break;
                default:
                    let u = options.readUnknownField;
                    if (u === "throw")
                        throw new globalThis.Error(`Unknown field ${fieldNo} (wire type ${wireType}) for ${this.typeName}`);
                    let d = reader.skip(wireType);
                    if (u !== false)
                        (u === true ? UnknownFieldHandler.onRead : u)(this.typeName, message, fieldNo, wireType, d);
            }
        }
        return message;
    }
    internalBinaryWrite(message: ForwardingPacket, writer: IBinaryWriter, options: BinaryWriteOptions): IBinaryWriter {
        /* repeated string forward_to = 1; */
        for (let i = 0; i < message.forwardTo.length; i++)
            writer.tag(1, WireType.LengthDelimited).string(message.forwardTo[i]);
        /* proto.packets.Packet packet = 2; */
        if (message.packet)
            Packet.internalBinaryWrite(message.packet, writer.tag(2, WireType.LengthDelimited).fork(), options).join();
        let u = options.writeUnknownFields;
        if (u !== false)
            (u == true ? UnknownFieldHandler.onWrite : u)(this.typeName, message, writer);
        return writer;
    }
}
/**
 * @generated MessageType for protobuf message proto.packets.ForwardingPacket
 */
export const ForwardingPacket = new ForwardingPacket$Type();
// @generated message type with reflection information, may provide speed optimized methods
class Packet$Type extends MessageType<Packet> {
    constructor() {
        super("proto.packets.Packet", [
            { no: 1, name: "token", kind: "scalar", T: 9 /*ScalarType.STRING*/ },
            { no: 2, name: "id", kind: "scalar", T: 9 /*ScalarType.STRING*/ },
            { no: 3, name: "from", kind: "scalar", T: 9 /*ScalarType.STRING*/ },
            { no: 4, name: "acknowledgement", kind: "message", oneof: "packet", T: () => Acknowledgement },
            { no: 5, name: "forwarding_packet", kind: "message", oneof: "packet", T: () => ForwardingPacket },
            { no: 6, name: "command", kind: "message", oneof: "packet", T: () => Command },
            { no: 7, name: "push", kind: "message", oneof: "packet", T: () => Push },
            { no: 8, name: "request", kind: "message", oneof: "packet", T: () => Request },
            { no: 9, name: "response", kind: "message", oneof: "packet", T: () => Response },
            { no: 10, name: "event", kind: "message", oneof: "packet", T: () => Event }
        ]);
    }
    create(value?: PartialMessage<Packet>): Packet {
        const message = { token: "", id: "", from: "", packet: { oneofKind: undefined } };
        globalThis.Object.defineProperty(message, MESSAGE_TYPE, { enumerable: false, value: this });
        if (value !== undefined)
            reflectionMergePartial<Packet>(this, message, value);
        return message;
    }
    internalBinaryRead(reader: IBinaryReader, length: number, options: BinaryReadOptions, target?: Packet): Packet {
        let message = target ?? this.create(), end = reader.pos + length;
        while (reader.pos < end) {
            let [fieldNo, wireType] = reader.tag();
            switch (fieldNo) {
                case /* string token */ 1:
                    message.token = reader.string();
                    break;
                case /* string id */ 2:
                    message.id = reader.string();
                    break;
                case /* string from */ 3:
                    message.from = reader.string();
                    break;
                case /* proto.packets.Acknowledgement acknowledgement */ 4:
                    message.packet = {
                        oneofKind: "acknowledgement",
                        acknowledgement: Acknowledgement.internalBinaryRead(reader, reader.uint32(), options, (message.packet as any).acknowledgement)
                    };
                    break;
                case /* proto.packets.ForwardingPacket forwarding_packet */ 5:
                    message.packet = {
                        oneofKind: "forwardingPacket",
                        forwardingPacket: ForwardingPacket.internalBinaryRead(reader, reader.uint32(), options, (message.packet as any).forwardingPacket)
                    };
                    break;
                case /* proto.packets.Command command */ 6:
                    message.packet = {
                        oneofKind: "command",
                        command: Command.internalBinaryRead(reader, reader.uint32(), options, (message.packet as any).command)
                    };
                    break;
                case /* proto.packets.Push push */ 7:
                    message.packet = {
                        oneofKind: "push",
                        push: Push.internalBinaryRead(reader, reader.uint32(), options, (message.packet as any).push)
                    };
                    break;
                case /* proto.packets.Request request */ 8:
                    message.packet = {
                        oneofKind: "request",
                        request: Request.internalBinaryRead(reader, reader.uint32(), options, (message.packet as any).request)
                    };
                    break;
                case /* proto.packets.Response response */ 9:
                    message.packet = {
                        oneofKind: "response",
                        response: Response.internalBinaryRead(reader, reader.uint32(), options, (message.packet as any).response)
                    };
                    break;
                case /* proto.packets.Event event */ 10:
                    message.packet = {
                        oneofKind: "event",
                        event: Event.internalBinaryRead(reader, reader.uint32(), options, (message.packet as any).event)
                    };
                    break;
                default:
                    let u = options.readUnknownField;
                    if (u === "throw")
                        throw new globalThis.Error(`Unknown field ${fieldNo} (wire type ${wireType}) for ${this.typeName}`);
                    let d = reader.skip(wireType);
                    if (u !== false)
                        (u === true ? UnknownFieldHandler.onRead : u)(this.typeName, message, fieldNo, wireType, d);
            }
        }
        return message;
    }
    internalBinaryWrite(message: Packet, writer: IBinaryWriter, options: BinaryWriteOptions): IBinaryWriter {
        /* string token = 1; */
        if (message.token !== "")
            writer.tag(1, WireType.LengthDelimited).string(message.token);
        /* string id = 2; */
        if (message.id !== "")
            writer.tag(2, WireType.LengthDelimited).string(message.id);
        /* string from = 3; */
        if (message.from !== "")
            writer.tag(3, WireType.LengthDelimited).string(message.from);
        /* proto.packets.Acknowledgement acknowledgement = 4; */
        if (message.packet.oneofKind === "acknowledgement")
            Acknowledgement.internalBinaryWrite(message.packet.acknowledgement, writer.tag(4, WireType.LengthDelimited).fork(), options).join();
        /* proto.packets.ForwardingPacket forwarding_packet = 5; */
        if (message.packet.oneofKind === "forwardingPacket")
            ForwardingPacket.internalBinaryWrite(message.packet.forwardingPacket, writer.tag(5, WireType.LengthDelimited).fork(), options).join();
        /* proto.packets.Command command = 6; */
        if (message.packet.oneofKind === "command")
            Command.internalBinaryWrite(message.packet.command, writer.tag(6, WireType.LengthDelimited).fork(), options).join();
        /* proto.packets.Push push = 7; */
        if (message.packet.oneofKind === "push")
            Push.internalBinaryWrite(message.packet.push, writer.tag(7, WireType.LengthDelimited).fork(), options).join();
        /* proto.packets.Request request = 8; */
        if (message.packet.oneofKind === "request")
            Request.internalBinaryWrite(message.packet.request, writer.tag(8, WireType.LengthDelimited).fork(), options).join();
        /* proto.packets.Response response = 9; */
        if (message.packet.oneofKind === "response")
            Response.internalBinaryWrite(message.packet.response, writer.tag(9, WireType.LengthDelimited).fork(), options).join();
        /* proto.packets.Event event = 10; */
        if (message.packet.oneofKind === "event")
            Event.internalBinaryWrite(message.packet.event, writer.tag(10, WireType.LengthDelimited).fork(), options).join();
        let u = options.writeUnknownFields;
        if (u !== false)
            (u == true ? UnknownFieldHandler.onWrite : u)(this.typeName, message, writer);
        return writer;
    }
}
/**
 * @generated MessageType for protobuf message proto.packets.Packet
 */
export const Packet = new Packet$Type();
