import './styles/main.css'
import { createApp } from 'vue'
import { createPinia } from 'pinia'
import { VueQueryPlugin } from '@tanstack/vue-query'
import App from './App.vue'
import router from './router'
import { vuetify } from './plugins/vuetify'
import { queryClient } from './plugins/queryClient'

createApp(App).use(createPinia()).use(vuetify).use(router).use(VueQueryPlugin, { queryClient }).mount('#app')
