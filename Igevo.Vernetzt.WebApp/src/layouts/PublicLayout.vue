<template>
  <v-main class="public-layout">
    <RouterView />
  </v-main>
</template>

<style scoped>
.public-layout {
  display: grid;
  min-height: 100dvh;
  place-items: center;
}
</style>
