export type CustomerBilling = {
  legalName: string;
  gstin: string;
  billingAddress: string;
  billingCity: string;
  billingState: string;
  billingPostalCode: string;
  billingCountry: string;
};

export const emptyCustomerBilling = (): CustomerBilling => ({
  legalName: '', gstin: '', billingAddress: '',
  billingCity: '', billingState: '', billingPostalCode: '', billingCountry: '',
});

export default function CustomerBillingFields({ value, onChange }: {
  value: CustomerBilling;
  onChange: (updated: CustomerBilling) => void;
}) {
  return <>
    <label>Legal / billing name<input maxLength={200} value={value.legalName}
      onChange={event => onChange({ ...value, legalName: event.target.value })} /></label>
    <label>GSTIN (India)<input maxLength={15}
      pattern="[0-9]{2}[A-Za-z]{5}[0-9]{4}[A-Za-z][0-9A-Za-z]Z[0-9A-Za-z]"
      title="Enter a valid 15-character Indian GSTIN"
      value={value.gstin} onChange={event =>
        onChange({ ...value, gstin: event.target.value.toUpperCase() })} /></label>
    <label>Billing address<textarea maxLength={500} value={value.billingAddress}
      onChange={event => onChange({ ...value, billingAddress: event.target.value })} /></label>
    <label>Billing city<input maxLength={100} value={value.billingCity}
      onChange={event => onChange({ ...value, billingCity: event.target.value })} /></label>
    <label>Billing state<input maxLength={100} value={value.billingState}
      onChange={event => onChange({ ...value, billingState: event.target.value })} /></label>
    <label>Billing postal code<input maxLength={20} value={value.billingPostalCode}
      onChange={event => onChange({ ...value, billingPostalCode: event.target.value })} /></label>
    <label>Billing country<input maxLength={100} value={value.billingCountry}
      onChange={event => onChange({ ...value, billingCountry: event.target.value })} /></label>
  </>;
}
