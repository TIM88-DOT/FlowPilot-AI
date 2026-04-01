import axios from "axios";

const publicApi = axios.create({
  baseURL: "/api/v1/public",
});

export default publicApi;
