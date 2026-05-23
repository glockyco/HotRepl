import { describe, expect, test } from "bun:test";
import { Value } from "typebox/value";
import {
  defaultLimits,
  ERROR_KINDS,
  type HandshakeMessage,
  HandshakeMessageSchema,
  MESSAGE_TYPES,
  PROTOCOL_VERSION,
} from "../src";

describe("protocol foundations", () => {
  test("exports the locked v2 constants", () => {
    expect(PROTOCOL_VERSION).toBe(2);
    expect(MESSAGE_TYPES.handshake).toBe("handshake");
    expect(MESSAGE_TYPES.sessionEvicted).toBe("session_evicted");
    expect(ERROR_KINDS).toEqual([
      "validation_failed",
      "precondition_failed",
      "conflict",
      "timeout",
      "cancelled",
      "busy",
      "unknown_command",
      "unsupported_operation",
      "artifact_missing",
      "invalid_request",
      "internal",
    ]);
  });

  test("validates an honest handshake with enforced limits", () => {
    const message: HandshakeMessage = {
      type: "handshake",
      protocolVersion: PROTOCOL_VERSION,
      host: { name: "Tests", version: "1.0.0", platform: "Unity Test" },
      evaluator: {
        name: "Roslyn.Script",
        languageVersion: "latest",
        persistentState: true,
        supportsCompletion: false,
        cancellation: "cooperative",
      },
      availableEvaluators: ["Roslyn.Script"],
      defaultUsings: ["System"],
      helpers: ["String[] Help()"],
      control: { supported: true, commandsListChanged: false, schemaValidation: false },
      limits: defaultLimits,
      enforces: [
        "maxMessageBytes",
        "maxQueuedCommands",
        "maxResultLength",
        "maxEnumerableElements",
        "maxJobConcurrency",
      ],
    };

    expect(Value.Check(HandshakeMessageSchema, message)).toBe(true);
  });
});
