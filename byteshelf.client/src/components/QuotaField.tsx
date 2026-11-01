import { useId } from 'react';
import { FieldLabel, Input, Select } from '../ui';
import { QUOTA_UNITS, type QuotaUnit } from '../lib/format';

interface QuotaFieldProps {
  amount: string;
  unit: QuotaUnit;
  onAmountChange: (amount: string) => void;
  onUnitChange: (unit: QuotaUnit) => void;
  error?: string;
}

/** A shelf's quota: an amount and its unit, empty for no limit. */
export function QuotaField({ amount, unit, onAmountChange, onUnitChange, error }: QuotaFieldProps) {
  const id = useId();

  return (
    <div className="space-y-1.5">
      <FieldLabel htmlFor={id}>Quota</FieldLabel>
      <div className="flex items-start gap-2">
        <div className="min-w-0 flex-1">
          <Input
            id={id}
            value={amount}
            onChange={(event) => onAmountChange(event.target.value)}
            placeholder="No limit"
            inputMode="decimal"
            autoComplete="off"
            error={error}
          />
        </div>
        <div className="w-24 shrink-0">
          <Select
            value={unit}
            onChange={onUnitChange}
            options={QUOTA_UNITS.map((value) => ({ value, label: value }))}
          />
        </div>
      </div>
    </div>
  );
}
