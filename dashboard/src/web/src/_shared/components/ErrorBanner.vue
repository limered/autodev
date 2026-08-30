<script setup>
defineProps({
  title: { type: String, required: true },
  message: { type: String, required: true }
})
</script>

<template>
  <section class="error-banner" role="alert">
    <strong>{{ title }}</strong>
    <p>{{ message }}</p>
  </section>
</template>

<style scoped>
/* Only the banner skin is shared here. Spacing and text sizing differ per theme
   (compact in-column alerts in DispatchView vs a roomier page-level banner in
   RunsList), so consumers keep margin/padding/font-size in their own scoped
   rules, which Vue applies to this component's root element. */
.error-banner {
  background: rgba(248, 81, 73, 0.12);
  border: 1px solid rgba(248, 81, 73, 0.35);
  border-radius: var(--radius);
}

.error-banner strong {
  display: block;
  color: var(--red);
  margin-bottom: 0.25rem;
}

.error-banner p {
  margin: 0;
  color: var(--text);
}
</style>
