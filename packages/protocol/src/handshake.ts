import { type Static, Type } from "typebox";
import { MESSAGE_TYPES, PROTOCOL_VERSION } from "./message-types";

export const CancellationModeSchema = Type.Union([
  Type.Literal("cooperative"),
  Type.Literal("hardAbort"),
  Type.Literal("unsupported"),
]);

export type CancellationMode = Static<typeof CancellationModeSchema>;

export const HostInfoSchema = Type.Object(
  {
    name: Type.String(),
    version: Type.String(),
    platform: Type.String(),
  },
  { additionalProperties: false },
);

export type HostInfo = Static<typeof HostInfoSchema>;

export const EvaluatorCapabilitiesSchema = Type.Object(
  {
    name: Type.String(),
    languageVersion: Type.String(),
    persistentState: Type.Boolean(),
    supportsCompletion: Type.Boolean(),
    cancellation: CancellationModeSchema,
  },
  { additionalProperties: false },
);

export type EvaluatorCapabilities = Static<typeof EvaluatorCapabilitiesSchema>;

export const ControlCapabilitiesSchema = Type.Object(
  {
    supported: Type.Boolean(),
    commandsListChanged: Type.Boolean(),
    schemaValidation: Type.Boolean(),
  },
  { additionalProperties: false },
);

export type ControlCapabilities = Static<typeof ControlCapabilitiesSchema>;

export const RuntimeLimitsSchema = Type.Object(
  {
    maxMessageBytes: Type.Integer({ minimum: 1 }),
    maxQueuedCommands: Type.Integer({ minimum: 0 }),
    maxResultLength: Type.Integer({ minimum: 1 }),
    maxEnumerableElements: Type.Integer({ minimum: 1 }),
    defaultEvalTimeoutMs: Type.Integer({ minimum: 1 }),
    maxJobConcurrency: Type.Integer({ minimum: 1 }),
  },
  { additionalProperties: false },
);

export type RuntimeLimits = Static<typeof RuntimeLimitsSchema>;

export const defaultLimits = {
  maxMessageBytes: 4 * 1024 * 1024,
  maxQueuedCommands: 32,
  maxResultLength: 100 * 1024,
  maxEnumerableElements: 100,
  defaultEvalTimeoutMs: 10_000,
  maxJobConcurrency: 1,
} as const satisfies RuntimeLimits;

export const EnforcedLimitSchema = Type.Union([
  Type.Literal("maxMessageBytes"),
  Type.Literal("maxQueuedCommands"),
  Type.Literal("maxResultLength"),
  Type.Literal("maxEnumerableElements"),
  Type.Literal("maxJobConcurrency"),
]);

export type EnforcedLimit = Static<typeof EnforcedLimitSchema>;

export const HandshakeMessageSchema = Type.Object(
  {
    type: Type.Literal(MESSAGE_TYPES.handshake),
    protocolVersion: Type.Literal(PROTOCOL_VERSION),
    host: HostInfoSchema,
    evaluator: EvaluatorCapabilitiesSchema,
    availableEvaluators: Type.Array(Type.String()),
    defaultUsings: Type.Array(Type.String()),
    helpers: Type.Array(Type.String()),
    control: ControlCapabilitiesSchema,
    limits: RuntimeLimitsSchema,
    enforces: Type.Array(EnforcedLimitSchema),
  },
  { additionalProperties: false },
);

export type HandshakeMessage = Static<typeof HandshakeMessageSchema>;
