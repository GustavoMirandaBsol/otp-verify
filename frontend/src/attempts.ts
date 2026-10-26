export const ATTEMPT_LIMITS = [3, 5, 10] as const;
export type AttemptLimit = (typeof ATTEMPT_LIMITS)[number];

export const isE164 = (value: string) => /^\+[1-9]\d{7,14}$/.test(value);

export const buildAttemptSearchPath = (phone: string, limit: AttemptLimit) =>
  `/verifications/attempts?phone=${encodeURIComponent(phone)}&limit=${limit}`;

export const createLatestRequestGate = () => {
  let generation = 0;
  return {
    start: () => ++generation,
    invalidate: () => { generation++; },
    isCurrent: (requestGeneration: number) => requestGeneration === generation,
  };
};

export type AttemptDiagnosis = {
  category: string;
  severity: string;
  explanation: string;
  recommendation: string;
};

export type VerificationAttempt = {
  sid: string;
  verificationSid: string;
  conversionStatus: string;
  channel: string;
  dateCreated: string | null;
  dateUpdated: string | null;
  messageStatus: string;
  attemptStatus: string;
  errorCode: string | null;
  to: string;
};

export type VerificationAttemptSearchItem = {
  attempt: VerificationAttempt;
  diagnosis: AttemptDiagnosis;
};
