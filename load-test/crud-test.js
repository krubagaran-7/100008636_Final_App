
import http from 'k6/http';
import { check, sleep } from 'k6';
import { Trend, Rate, Counter } from 'k6/metrics';

// API address; can be changed when testing Docker or Kubernetes
const BASE_URL = __ENV.BASE_URL || 'http://localhost:5000';

// Custom performance metrics
const createTime = new Trend('create_response_time');
const readTime = new Trend('read_response_time');
const updateTime = new Trend('update_response_time');
const deleteTime = new Trend('delete_response_time');

const errorRate = new Rate('request_errors');
const completedCRUD = new Counter('completed_crud_cycles');

export const options = {
  stages: [
    { duration: '10s', target: 100 },
    { duration: '10s', target: 500 },
    { duration: '30s', target: 500 },
    { duration: '10s', target: 0 },
  ],

  thresholds: {
    http_req_duration: ['p(95)<500'],
    request_errors: ['rate<0.05'],
    checks: ['rate>0.95'],
  },
};

export default function () {
  const headers = {
    'Content-Type': 'application/json',
  };

  // 1. CREATE a new note
  const newNote = {
    title: `Load Test VU ${__VU} Iteration ${__ITER}`,
    content: 'Created during k6 performance testing',
  };

  const createResponse = http.post(
    `${BASE_URL}/notes`,
    JSON.stringify(newNote),
    { headers }
  );

  createTime.add(createResponse.timings.duration);

  const createOK = check(createResponse, {
    'CREATE: status is 201': (r) => r.status === 201,
    'CREATE: response contains ID': (r) => {
      try {
        return Number.isInteger(JSON.parse(r.body).id);
      } catch {
        return false;
      }
    },
  });

  errorRate.add(!createOK);

  if (!createOK) {
    return;
  }

  const noteId = JSON.parse(createResponse.body).id;

  sleep(0.2);

  // 2. READ all notes
  const listResponse = http.get(`${BASE_URL}/notes`);

  readTime.add(listResponse.timings.duration);

  const listOK = check(listResponse, {
    'READ ALL: status is 200': (r) => r.status === 200,
  });

  errorRate.add(!listOK);

  sleep(0.2);

  // 3. READ one note
  const singleResponse = http.get(`${BASE_URL}/notes/${noteId}`);

  readTime.add(singleResponse.timings.duration);

  const singleOK = check(singleResponse, {
    'READ ONE: status is 200': (r) => r.status === 200,
  });

  errorRate.add(!singleOK);

  sleep(0.2);

  // 4. UPDATE the note
  const updatedNote = {
    title: `Updated by VU ${__VU}`,
    content: 'Updated during k6 performance testing',
  };

  const updateResponse = http.put(
    `${BASE_URL}/notes/${noteId}`,
    JSON.stringify(updatedNote),
    { headers }
  );

  updateTime.add(updateResponse.timings.duration);

  const updateOK = check(updateResponse, {
    'UPDATE: successful response': (r) =>
      r.status === 200 || r.status === 204,
  });

  errorRate.add(!updateOK);

  sleep(0.2);

  // 5. DELETE the note
  const deleteResponse = http.del(
    `${BASE_URL}/notes/${noteId}`
  );

  deleteTime.add(deleteResponse.timings.duration);

  const deleteOK = check(deleteResponse, {
    'DELETE: successful response': (r) =>
      r.status === 200 || r.status === 204,
  });

  errorRate.add(!deleteOK);

  if (createOK && listOK && singleOK && updateOK && deleteOK) {
    completedCRUD.add(1);
  }

  sleep(1);
}
